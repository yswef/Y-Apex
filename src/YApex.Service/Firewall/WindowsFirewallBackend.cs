// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using YApex.Core.Abstractions;
using YApex.Core.Domain;
using YApex.Core.Security;

namespace YApex.Service.Firewall;

public sealed class WindowsFirewallBackend : IFirewallBackend, IDisposable
{
    private const string PolicyProgramId = "HNetCfg.FwPolicy2";
    private const string RuleProgramId = "HNetCfg.FWRule";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<ManagedFirewallRule>> GetOwnedRulesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(ReadOwnedRules, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ManagedFirewallRule>> GetOwnedRulesForProgramAsync(
        string normalizedProgramPath,
        CancellationToken cancellationToken)
    {
        var path = WindowsProgramPath.Normalize(normalizedProgramPath);
        var allRules = await GetOwnedRulesAsync(cancellationToken).ConfigureAwait(false);
        return allRules
            .Where(rule => string.Equals(rule.ProgramPath, path, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public async Task ReplaceProgramRulesAsync(
        string normalizedProgramPath,
        IReadOnlyList<ManagedFirewallRule> replacement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var path = WindowsProgramPath.Normalize(normalizedProgramPath);
        ValidateReplacement(path, replacement);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => ReplaceProgramRules(path, replacement), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveOwnedRulesAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(RemoveAllOwnedRules, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static IReadOnlyList<ManagedFirewallRule> ReadOwnedRules()
    {
        var policy = CreatePolicy();
        dynamic dynamicPolicy = policy;
        object? collection = null;
        try
        {
            collection = dynamicPolicy.Rules;
            var result = new List<ManagedFirewallRule>();
            foreach (var ruleObject in (IEnumerable)collection)
            {
                if (ruleObject is null)
                {
                    continue;
                }

                try
                {
                    if (TryReadOwnedRule(ruleObject, out var rule))
                    {
                        result.Add(rule);
                    }
                }
                finally
                {
                    Release(ruleObject);
                }
            }

            return result;
        }
        finally
        {
            Release(collection);
            Release(policy);
        }
    }

    private static bool TryReadOwnedRule(object ruleObject, out ManagedFirewallRule rule)
    {
        rule = null!;
        dynamic source = ruleObject;
        var name = Convert.ToString((object?)source.Name, CultureInfo.InvariantCulture);
        var description = Convert.ToString((object?)source.Description, CultureInfo.InvariantCulture);
        var pathValue = Convert.ToString((object?)source.ApplicationName, CultureInfo.InvariantCulture);
        if (name is null || description is null || pathValue is null ||
            !name.StartsWith("Y-Apex ", StringComparison.Ordinal) ||
            !description.StartsWith(RuleFactory.OwnershipMarker + ";", StringComparison.Ordinal))
        {
            return false;
        }

        string path;
        try
        {
            path = WindowsProgramPath.Normalize(pathValue);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var directionValue = Convert.ToInt32((object?)source.Direction, CultureInfo.InvariantCulture);
        var actionValue = Convert.ToInt32((object?)source.Action, CultureInfo.InvariantCulture);
        if (directionValue is not (1 or 2) || actionValue is not (0 or 1))
        {
            return false;
        }

        var direction = directionValue == 1 ? FirewallDirection.Inbound : FirewallDirection.Outbound;
        var action = actionValue == 0 ? FirewallAction.Block : FirewallAction.Allow;
        var metadata = description.Split(';', StringSplitOptions.None);
        if (metadata.Length != 5 ||
            !string.Equals(metadata[0], RuleFactory.OwnershipMarker, StringComparison.Ordinal) ||
            !string.Equals(metadata[2], "path=" + WindowsProgramPath.GetStableHash(path), StringComparison.Ordinal) ||
            !metadata[3].StartsWith("file=", StringComparison.Ordinal) ||
            !metadata[4].StartsWith("signature=", StringComparison.Ordinal))
        {
            return false;
        }

        var fileHashValue = metadata[3][5..];
        var signatureValue = metadata[4][10..];
        if (fileHashValue != "unknown" && (fileHashValue.Length != 64 || !fileHashValue.All(Uri.IsHexDigit)))
        {
            return false;
        }

        if (!Enum.TryParse<SignatureState>(signatureValue, ignoreCase: false, out var signature))
        {
            return false;
        }

        ProgramFileIdentity identity;
        try
        {
            identity = new ProgramFileIdentity(path, fileHashValue == "unknown" ? null : fileHashValue, signature);
        }
        catch (ArgumentException)
        {
            return false;
        }

        ManagedFirewallRule expected;
        try
        {
            expected = RuleFactory.CreateOwnedRule(path, direction, action, identity);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!string.Equals(name, expected.Name, StringComparison.Ordinal) ||
            !string.Equals(description, expected.Description, StringComparison.Ordinal))
        {
            return false;
        }

        var enabled = Convert.ToBoolean((object?)source.Enabled, CultureInfo.InvariantCulture);
        rule = expected with { Enabled = enabled };
        return true;
    }

    private static void ReplaceProgramRules(string path, IReadOnlyList<ManagedFirewallRule> replacement)
    {
        var policy = CreatePolicy();
        dynamic dynamicPolicy = policy;
        object? collection = null;
        try
        {
            collection = dynamicPolicy.Rules;
            var names = ReadOwnedRules()
                .Where(rule => string.Equals(rule.ProgramPath, path, StringComparison.OrdinalIgnoreCase))
                .Select(rule => rule.Name)
                .ToArray();
            foreach (var name in names)
            {
                dynamic rules = collection;
                rules.Remove(name);
            }

            foreach (var rule in replacement)
            {
                AddRule(collection, rule);
            }
        }
        finally
        {
            Release(collection);
            Release(policy);
        }
    }

    private static void RemoveAllOwnedRules()
    {
        var policy = CreatePolicy();
        dynamic dynamicPolicy = policy;
        object? collection = null;
        try
        {
            collection = dynamicPolicy.Rules;
            var names = ReadOwnedRules().Select(rule => rule.Name).ToArray();
            foreach (var name in names)
            {
                dynamic rules = collection;
                rules.Remove(name);
            }
        }
        finally
        {
            Release(collection);
            Release(policy);
        }
    }

    private static void AddRule(object collection, ManagedFirewallRule rule)
    {
        var type = Type.GetTypeFromProgID(RuleProgramId, throwOnError: true)
            ?? throw new InvalidOperationException("The Windows Firewall rule COM class is unavailable.");
        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("The Windows Firewall rule could not be created.");
        try
        {
            dynamic createdRule = instance;
            createdRule.Name = rule.Name;
            createdRule.Description = rule.Description;
            createdRule.ApplicationName = rule.ProgramPath;
            createdRule.Direction = rule.Direction == FirewallDirection.Inbound ? 1 : 2;
            createdRule.Action = rule.Action == FirewallAction.Block ? 0 : 1;
            createdRule.Enabled = rule.Enabled;
            createdRule.Profiles = int.MaxValue;
            createdRule.Protocol = 256;
            createdRule.InterfaceTypes = "All";
            createdRule.Grouping = "Y-Apex";
            dynamic rules = collection;
            rules.Add(instance);
        }
        finally
        {
            Release(instance);
        }
    }

    private static void ValidateReplacement(string path, IReadOnlyList<ManagedFirewallRule> replacement)
    {
        if (replacement.Any(rule => !RuleFactory.IsOwnedRule(rule) ||
                                    !string.Equals(rule.ProgramPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Replacement rules must be Y-Apex rules for the requested program path.", nameof(replacement));
        }

        var validShape = replacement.Count switch
        {
            0 => true,
            1 => replacement[0].Direction == FirewallDirection.Outbound && replacement[0].Action == FirewallAction.Allow,
            2 => replacement.Select(rule => rule.Direction).Distinct().Count() == 2 &&
                 replacement.Select(rule => rule.Action).Distinct().Count() == 1,
            _ => false
        };
        if (!validShape)
        {
            throw new ArgumentException("A program may have no Y-Apex rules, one outbound allow rule, or a matching inbound/outbound pair.", nameof(replacement));
        }

        foreach (var rule in replacement)
        {
            var identity = new ProgramFileIdentity(path, rule.FileSha256, rule.Signature);
            var expected = RuleFactory.CreateOwnedRule(path, rule.Direction, rule.Action, identity);
            if (rule.RuleId != expected.RuleId || rule.Name != expected.Name || rule.Description != expected.Description)
            {
                throw new ArgumentException("A replacement rule has invalid ownership metadata.", nameof(replacement));
            }
        }
    }

    private static object CreatePolicy()
    {
        var type = Type.GetTypeFromProgID(PolicyProgramId, throwOnError: true)
            ?? throw new InvalidOperationException("The Windows Firewall policy COM class is unavailable.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("The Windows Firewall policy could not be opened.");
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
