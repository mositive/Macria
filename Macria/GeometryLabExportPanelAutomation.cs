using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Automation;

namespace Macria;

internal sealed class GeometryLabExportPanelSetupResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = "";
    public AutomationElement? Panel { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Label- and role-based automation for CATIA's built-in Export panel. It never uses
/// coordinates or sends keyboard input, and refuses to invoke OK until every editable
/// destination value has been read back from the panel.
/// </summary>
internal static class GeometryLabExportPanelAutomation
{
    private static readonly string[] DiscoveryLabels =
        { "Format", "Target", "Location", "Filename", "Save report", "Simulate" };
    private static readonly object DiscoveryGate = new();
    private static readonly Dictionary<IntPtr, DateTime> LastScopedDiscoveryUtc = new();
    private const int DiscoveryNodeBudget = 2000;
    private const int DiscoveryTimeBudgetMilliseconds = 2000;

    internal static AutomationElement? FindExportPanel(IntPtr catiaWindow, IList<string> diagnostics)
    {
        try
        {
            AutomationElement catia = AutomationElement.FromHandle(catiaWindow);
            int catiaProcessId = catia.Current.ProcessId;
            AutomationElementCollection candidates = AutomationElement.RootElement.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "Export"));

            // This is diagnostic-only. A structurally discovered element is never
            // returned to the caller, configured, or invoked.
            if (candidates.Count == 0 && TryBeginScopedDiscovery(catiaWindow))
                DiscoverStructuralCandidates(catia, catiaWindow, diagnostics);

            var acceptable = new List<AutomationElement>();
            foreach (AutomationElement candidate in candidates)
            {
                PanelCandidateEvidence evidence = EvaluateCandidate(candidate, catia, catiaProcessId);
                diagnostics.Add(evidence.Diagnostic);
                if (evidence.IsAcceptable) acceptable.Add(candidate);
            }

            if (acceptable.Count == 1)
            {
                diagnostics.Add("PANEL_FOUND accepted=1.");
                return acceptable[0];
            }
            if (acceptable.Count > 1)
                diagnostics.Add("PANEL_FOUND failed reason=multiple_complete_candidates count=" + acceptable.Count + ".");
            else
                diagnostics.Add("PANEL_FOUND failed reason=no_complete_visible_candidate.");
        }
        catch (Exception exception)
        {
            diagnostics.Add("Export panel search failed: " + Short(exception.Message));
        }
        return null;
    }

    private static bool TryBeginScopedDiscovery(IntPtr catiaWindow)
    {
        lock (DiscoveryGate)
        {
            DateTime now = DateTime.UtcNow;
            if (LastScopedDiscoveryUtc.TryGetValue(catiaWindow, out DateTime last) &&
                now - last < TimeSpan.FromSeconds(10))
                return false;
            LastScopedDiscoveryUtc[catiaWindow] = now;
            return true;
        }
    }

    private static void DiscoverStructuralCandidates(
        AutomationElement catia,
        IntPtr catiaWindow,
        IList<string> diagnostics)
    {
        var discoveryDiagnostics = new List<string>();
        try
        {
            System.Windows.Rect catiaBounds = catia.Current.BoundingRectangle;
            int catiaProcessId = catia.Current.ProcessId;
            discoveryDiagnostics.Add("DISCOVERY_SCOPE catiaHwnd=" + catiaWindow +
                                     " processId=" + catiaProcessId +
                                     " boundingRectangle=" + catiaBounds +
                                     " nodeBudget=" + DiscoveryNodeBudget +
                                     " timeBudgetMs=" + DiscoveryTimeBudgetMilliseconds + ".");
            var discovered = new List<StructuralCandidate>();
            AutomationElementCollection topLevel = AutomationElement.RootElement.FindAll(
                TreeScope.Children, Condition.TrueCondition);
            int scannedTopLevel = 0;
            int scannedNodes = 0;
            bool budgetExceeded = false;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            foreach (AutomationElement top in topLevel)
            {
                if (++scannedTopLevel > 64) break;
                if (!IsVisibleAndEnabled(top)) continue;
                if (IsExcludedDiscoveryProcess(top, catiaProcessId, catiaBounds)) continue;
                if (!BoundsRelatedToCatia(SafeBoundsValue(top), catiaBounds)) continue;

                foreach (AutomationElement item in DescendantsUpTo(top, DiscoveryNodeBudget - scannedNodes))
                {
                    if (++scannedNodes > DiscoveryNodeBudget || stopwatch.ElapsedMilliseconds > DiscoveryTimeBudgetMilliseconds)
                    {
                        budgetExceeded = true;
                        break;
                    }
                    if (!IsVisibleAndEnabled(item)) continue;
                    if (IsExcludedDiscoveryProcess(item, catiaProcessId, catiaBounds)) continue;
                    if (!BoundsRelatedToCatia(SafeBoundsValue(item), catiaBounds)) continue;

                    string[] labels = DiscoveryLabels.Where(label => IsExactDiscoveryLabel(SafeName(item), label)).ToArray();
                    if (labels.Length == 0) continue;

                    AutomationElement? current = item;
                    for (int depth = 0; current != null && depth < 6; depth++)
                    {
                        if (IsVisibleAndEnabled(current))
                        {
                            StructuralCandidate? candidate = discovered.FirstOrDefault(existing => existing.Element.Equals(current));
                            if (candidate == null)
                            {
                                candidate = new StructuralCandidate(current);
                                discovered.Add(candidate);
                            }
                            foreach (string label in labels) candidate.AddLabel(label, item);
                        }
                        current = TreeWalker.ControlViewWalker.GetParent(current);
                    }
                }
                if (budgetExceeded) break;
            }
            if (scannedNodes >= DiscoveryNodeBudget || stopwatch.ElapsedMilliseconds >= DiscoveryTimeBudgetMilliseconds)
                budgetExceeded = true;

            StructuralCandidate[] matches = discovered
                .Where(candidate => candidate.Labels.Count >= 2)
                .Select(candidate => candidate.WithNearestCommonContainer())
                .Where(candidate => BoundsRelatedToCatia(SafeBoundsValue(candidate.CommonContainer), catiaBounds))
                .OrderBy(candidate => BoundingArea(candidate.CommonContainer))
                .Take(12)
                .ToArray();
            foreach (StructuralCandidate candidate in matches)
                discoveryDiagnostics.Add(DescribeDiscoveryCandidate(candidate));

            if (matches.Length == 0)
                discoveryDiagnostics.Add("DISCOVERY_CANDIDATE none labels_required=2.");
            else if (discovered.Count(candidate => candidate.Labels.Count >= 2) > matches.Length)
                discoveryDiagnostics.Add("DISCOVERY_CANDIDATE output_limited=" + matches.Length + ".");
            if (budgetExceeded)
                discoveryDiagnostics.Add("DIAGNOSTIC_BUDGET_EXCEEDED scannedNodes=" + scannedNodes +
                                         " elapsedMs=" + stopwatch.ElapsedMilliseconds + ".");

            AddScopedFallbackSummaries(catia, catiaBounds, catiaProcessId, discoveryDiagnostics, stopwatch, ref scannedNodes, ref budgetExceeded);
        }
        catch (Exception exception)
        {
            discoveryDiagnostics.Add("DISCOVERY_CANDIDATE failed reason=discovery_error detail=" + Short(exception.Message));
        }
        WriteDiscoveryDiagnostics(discoveryDiagnostics);
        foreach (string item in discoveryDiagnostics) diagnostics.Add(item);
    }

    private static IEnumerable<AutomationElement> DescendantsUpTo(AutomationElement root, int maximum)
    {
        var stack = new Stack<AutomationElement>();
        AutomationElement? child = TreeWalker.ControlViewWalker.GetFirstChild(root);
        if (child != null) stack.Push(child);
        int count = 0;
        while (stack.Count > 0 && count++ < maximum)
        {
            AutomationElement current = stack.Pop();
            yield return current;

            AutomationElement? sibling = TreeWalker.ControlViewWalker.GetNextSibling(current);
            if (sibling != null) stack.Push(sibling);
            AutomationElement? firstChild = TreeWalker.ControlViewWalker.GetFirstChild(current);
            if (firstChild != null) stack.Push(firstChild);
        }
    }

    private static string DescribeDiscoveryCandidate(StructuralCandidate candidate)
    {
        AutomationElement element = candidate.CommonContainer;
        AutomationElement[] controls = DescendantsUpTo(element, 500)
            .Where(item => item.Current.ControlType == ControlType.Edit ||
                           item.Current.ControlType == ControlType.ComboBox ||
                           item.Current.ControlType == ControlType.Button)
            .Take(40)
            .ToArray();
        return "DISCOVERY_CANDIDATE name=" + ValueOrEmpty(SafeName(element)) +
               ", localizedControlType=" + ValueOrEmpty(SafeLocalizedControlType(element)) +
               ", controlType=" + SafeControlType(element) +
               ", automationId=" + ValueOrEmpty(SafeAutomationId(element)) +
               ", className=" + ValueOrEmpty(SafeClassName(element)) +
               ", frameworkId=" + ValueOrEmpty(SafeFrameworkId(element)) +
               ", processId=" + SafeProcessId(element) +
               ", isOffscreen=" + SafeIsOffscreen(element) +
               ", isEnabled=" + SafeIsEnabled(element) +
               ", boundingRectangle=" + SafeBounds(element) +
               ", labels=" + string.Join(",", candidate.Labels.OrderBy(label => label)) +
               ", nearestCommonContainer=" + DescribeDiscoveryElement(element) +
               ", parents=" + ParentChain(element) +
               ", controls=[" + string.Join(";", controls.Select(DescribeDiscoveryControl)) + "]";
    }

    private static string DescribeDiscoveryControl(AutomationElement element) =>
        "name=" + ValueOrEmpty(SafeName(element)) +
        "|automationId=" + ValueOrEmpty(SafeAutomationId(element)) +
        "|className=" + ValueOrEmpty(SafeClassName(element)) +
        "|controlType=" + SafeControlType(element) +
        "|patterns=" + SupportedPatterns(element);

    private static string DescribeDiscoveryElement(AutomationElement element) =>
        "name=" + ValueOrEmpty(SafeName(element)) +
        "|controlType=" + SafeControlType(element) +
        "|automationId=" + ValueOrEmpty(SafeAutomationId(element)) +
        "|className=" + ValueOrEmpty(SafeClassName(element)) +
        "|frameworkId=" + ValueOrEmpty(SafeFrameworkId(element)) +
        "|processId=" + SafeProcessId(element) +
        "|boundingRectangle=" + SafeBounds(element);

    private static void AddScopedFallbackSummaries(
        AutomationElement catia,
        System.Windows.Rect catiaBounds,
        int catiaProcessId,
        IList<string> diagnostics,
        System.Diagnostics.Stopwatch stopwatch,
        ref int scannedNodes,
        ref bool budgetExceeded)
    {
        int summaryCount = 0;
        foreach (AutomationElement item in DescendantsUpTo(catia, DiscoveryNodeBudget - scannedNodes))
        {
            if (++scannedNodes > DiscoveryNodeBudget || stopwatch.ElapsedMilliseconds > DiscoveryTimeBudgetMilliseconds)
            {
                budgetExceeded = true;
                return;
            }
            if (!IsVisibleAndEnabled(item) ||
                IsExcludedDiscoveryProcess(item, catiaProcessId, catiaBounds) ||
                !BoundsRelatedToCatia(SafeBoundsValue(item), catiaBounds)) continue;

            int labelCount = DiscoveryLabels.Count(label => IsExactDiscoveryLabel(SafeName(item), label));
            if (labelCount >= 2) continue;
            diagnostics.Add("DISCOVERY_SCOPE_ELEMENT " + DescribeDiscoveryElement(item));
            if (++summaryCount == 20) return;
        }
    }

    private static bool IsExcludedDiscoveryProcess(
        AutomationElement element,
        int catiaProcessId,
        System.Windows.Rect catiaBounds)
    {
        int processId = SafeProcessId(element);
        if (processId == Environment.ProcessId) return true;
        string processName = ProcessName(processId);
        if (processName.Contains("code", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("codex", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(SafeFrameworkId(element), "Chrome", StringComparison.OrdinalIgnoreCase) &&
            !BoundsRelatedToCatia(SafeBoundsValue(element), catiaBounds)) return true;
        return false;
    }

    private static string ProcessName(int processId)
    {
        try { return System.Diagnostics.Process.GetProcessById(processId).ProcessName; }
        catch { return ""; }
    }

    private static bool IsExactDiscoveryLabel(string name, string label) =>
        string.Equals(NormalizeDiscoveryLabel(name), NormalizeDiscoveryLabel(label), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDiscoveryLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string normalized = string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.TrimEnd(':').Trim();
    }

    private static bool BoundsRelatedToCatia(System.Windows.Rect candidate, System.Windows.Rect catia)
    {
        if (candidate.IsEmpty || catia.IsEmpty || candidate.Width <= 0 || candidate.Height <= 0) return false;
        if (catia.Contains(candidate)) return true;
        System.Windows.Rect overlap = System.Windows.Rect.Intersect(candidate, catia);
        if (overlap.IsEmpty) return false;
        double candidateArea = candidate.Width * candidate.Height;
        double overlapArea = overlap.Width * overlap.Height;
        return overlapArea >= 256 && overlapArea / candidateArea >= 0.20;
    }

    private static System.Windows.Rect SafeBoundsValue(AutomationElement element)
    {
        try { return element.Current.BoundingRectangle; }
        catch { return System.Windows.Rect.Empty; }
    }

    private static void WriteDiscoveryDiagnostics(IList<string> diagnostics)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Macria", "GeometryLabDiagnostics");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                "export-panel-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".txt");
            diagnostics.Add("DIAGNOSTIC_FILE path=" + path + ".");
            File.WriteAllLines(path, diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add("DIAGNOSTIC_FILE failed detail=" + Short(exception.Message));
        }
    }

    private static bool IsVisibleAndEnabled(AutomationElement element) =>
        !SafeIsOffscreen(element) && SafeIsEnabled(element);

    private static double BoundingArea(AutomationElement element)
    {
        try
        {
            var bounds = element.Current.BoundingRectangle;
            return bounds.IsEmpty ? double.MaxValue : bounds.Width * bounds.Height;
        }
        catch { return double.MaxValue; }
    }

    private static PanelCandidateEvidence EvaluateCandidate(
        AutomationElement candidate,
        AutomationElement catia,
        int catiaProcessId)
    {
        try
        {
            bool visible = !candidate.Current.IsOffscreen;
            bool enabled = candidate.Current.IsEnabled;
            bool sameProcess = candidate.Current.ProcessId == catiaProcessId;
            string parentChain = ParentChain(candidate);
            bool related = sameProcess || IsRelatedToCatia(candidate, catia);
            string[] fields = { "Format", "Target", "Location", "Filename" };
            var controls = fields.ToDictionary(label => label, label => FindLabeledControl(candidate, label));
            string[] foundFields = controls.Where(pair => pair.Value != null).Select(pair => pair.Key).ToArray();
            AutomationElement? okButton = FindButton(candidate, "OK");
            AutomationElement? cancelButton = FindButton(candidate, "Cancel");
            bool hasOk = okButton != null;
            bool hasCancel = cancelButton != null;
            bool hasSimulate = FindButton(candidate, "Simulate") != null;
            bool hasSaveReport = candidate.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox))
                .Cast<AutomationElement>().Any(item => ContainsLabel(item.Current.Name, "Save report"));
            bool acceptable = visible && enabled && foundFields.Length == fields.Length && hasOk && hasCancel;

            var failures = new List<string>();
            if (!visible) failures.Add("panel_offscreen");
            if (!enabled) failures.Add("panel_disabled");
            foreach (string label in fields.Where(label => controls[label] == null))
                failures.Add("missing_field=" + label + " seen_roles=" + SeenLabelRoles(candidate, label));
            if (!hasOk) failures.Add("missing_OK");
            if (!hasCancel) failures.Add("missing_Cancel");

            string diagnostic = "PANEL_CANDIDATE processId=" + candidate.Current.ProcessId +
                                ", automationId=" + ValueOrEmpty(candidate.Current.AutomationId) +
                                ", frameworkId=" + ValueOrEmpty(candidate.Current.FrameworkId) +
                                ", controlType=" + candidate.Current.ControlType.ProgrammaticName +
                                ", isOffscreen=" + candidate.Current.IsOffscreen +
                                ", isEnabled=" + enabled +
                                ", sameCatiaProcess=" + sameProcess +
                                ", relatedToCatia=" + related +
                                ", fields=" + string.Join(";", fields.Select(label => DescribeControl(label, controls[label]))) +
                                ", OK=" + DescribeControl("OK", okButton) +
                                ", Cancel=" + DescribeControl("Cancel", cancelButton) +
                                ", Simulate=" + hasSimulate + ", SaveReport=" + hasSaveReport +
                                ", parents=" + parentChain +
                                (failures.Count == 0 ? ", accepted=true" : ", accepted=false reason=" + string.Join("|", failures));
            if (foundFields.Length == fields.Length)
                diagnostic += "\nFIELDS_FOUND fields=" + string.Join(",", foundFields) + ".";
            return new PanelCandidateEvidence(acceptable, diagnostic);
        }
        catch (Exception exception)
        {
            return new PanelCandidateEvidence(false, "PANEL_CANDIDATE failed reason=inspection_error detail=" + Short(exception.Message));
        }
    }

    private static bool IsRelatedToCatia(AutomationElement candidate, AutomationElement catia)
    {
        try
        {
            int catiaHandle = catia.Current.NativeWindowHandle;
            for (AutomationElement? current = candidate; current != null; current = TreeWalker.ControlViewWalker.GetParent(current))
            {
                if (current.Current.NativeWindowHandle == catiaHandle) return true;
            }
        }
        catch { }
        return false;
    }

    private static string ParentChain(AutomationElement element)
    {
        var values = new List<string>();
        try
        {
            for (AutomationElement? current = element; current != null && values.Count < 6;
                 current = TreeWalker.ControlViewWalker.GetParent(current))
            {
                values.Add(ValueOrEmpty(current.Current.Name) + "/" + current.Current.ControlType.ProgrammaticName);
            }
        }
        catch { }
        return string.Join(" > ", values);
    }

    internal static GeometryLabExportPanelSetupResult Configure(
        AutomationElement panel,
        string workspaceDirectory,
        string fileStem)
    {
        var diagnostics = new List<string>();
        try
        {
            string expectedDirectory = Path.GetFullPath(workspaceDirectory);
            if (!Guid.TryParse(Path.GetFileName(expectedDirectory), out _) ||
                !string.Equals(fileStem, "input", StringComparison.Ordinal))
                return Failure(panel, diagnostics, "Temporary export target is not an owned GeometryLab input workspace.");

            if (!SetAndVerify(panel, "Format", "STEP (*.stp)", diagnostics))
                return Failure(panel, diagnostics, "STEP format could not be verified.");
            if (!SetAndVerify(panel, "Target", "File on disk", diagnostics))
                return Failure(panel, diagnostics, "File on disk target could not be verified.");
            if (!SetAndVerify(panel, "Location", expectedDirectory, diagnostics, pathValue: true))
                return Failure(panel, diagnostics, "Temporary GeometryLab location could not be verified.");
            if (!SetAndVerify(panel, "Filename", fileStem, diagnostics))
                return Failure(panel, diagnostics, "Temporary STEP filename could not be verified.");

            ClearSaveReport(panel, diagnostics);
            if (FindButton(panel, "OK") == null)
                return Failure(panel, diagnostics, "Verified Export panel has no OK button.");

            diagnostics.Add("Export panel fields were read back and verified before OK.");
            return new GeometryLabExportPanelSetupResult
            {
                IsSuccess = true,
                Panel = panel,
                Diagnostics = diagnostics
            };
        }
        catch (Exception exception)
        {
            diagnostics.Add("Panel configuration failed: " + Short(exception.Message));
            return Failure(panel, diagnostics, "Export panel configuration failed.");
        }
    }

    internal static bool TryInvokeOk(AutomationElement panel, IList<string> diagnostics) =>
        TryInvokeButton(panel, "OK", diagnostics);

    internal static void TryCancel(AutomationElement? panel, IList<string> diagnostics)
    {
        if (panel != null) TryInvokeButton(panel, "Cancel", diagnostics);
    }

    private static bool SetAndVerify(
        AutomationElement panel,
        string label,
        string expected,
        IList<string> diagnostics,
        bool pathValue = false)
    {
            AutomationElement? control = FindLabeledControl(panel, label);
            if (control == null)
            {
                diagnostics.Add("FIELDS_FOUND failed field=" + label + " seen_roles=" + SeenLabelRoles(panel, label) + ".");
                return false;
            }

        string? actual = ReadValue(control);
        if (!ValuesMatch(actual, expected, pathValue))
        {
            if (!TrySetValue(control, expected, out string setFailure))
            {
                diagnostics.Add("VALUES_SET failed field=" + label + " reason=" + setFailure + ".");
                return false;
            }
            diagnostics.Add("VALUES_SET field=" + label + ".");
            actual = ReadValue(control);
        }

        bool verified = ValuesMatch(actual, expected, pathValue);
        if (verified)
            diagnostics.Add("VALUES_VERIFIED field=" + label + ".");
        else
            diagnostics.Add("VALUES_VERIFIED failed field=" + label + " actual=" + ValueOrNull(actual) +
                            " expected=" + expected + ".");
        return verified;
    }

    private static AutomationElement? FindLabeledControl(AutomationElement panel, string label)
    {
        AutomationElementCollection all = panel.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        foreach (AutomationElement item in all)
        {
            if (!IsEditableOrSelectable(item) || !ContainsLabel(item.Current.Name, label)) continue;
            return item;
        }

        foreach (AutomationElement labelElement in all)
        {
            if (labelElement.Current.ControlType != ControlType.Text ||
                !ContainsLabel(labelElement.Current.Name, label)) continue;

            AutomationElement? parent = TreeWalker.ControlViewWalker.GetParent(labelElement);
            for (int depth = 0; parent != null && depth < 3; depth++)
            {
                // Only inspect immediate role siblings of a label. Searching the whole
                // ancestor subtree could select another field and mutate it incorrectly.
                AutomationElement[] siblings = parent.FindAll(TreeScope.Children, Condition.TrueCondition)
                    .Cast<AutomationElement>().Where(IsEditableOrSelectable).ToArray();
                if (siblings.Length == 1) return siblings[0];
                parent = TreeWalker.ControlViewWalker.GetParent(parent);
            }
        }
        return null;
    }

    private static bool IsEditableOrSelectable(AutomationElement item)
    {
        ControlType type = item.Current.ControlType;
        return type == ControlType.Edit || type == ControlType.ComboBox;
    }

    private static bool TrySetValue(AutomationElement control, string expected, out string failure)
    {
        failure = "ValuePattern_missing supportedPatterns=" + SupportedPatterns(control);
        if (control.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
        {
            try
            {
                ((ValuePattern)pattern).SetValue(expected);
                return true;
            }
            catch (Exception exception)
            {
                failure = "SetValue_error detail=" + Short(exception.Message);
            }
        }
        return false;
    }

    private static string? ReadValue(AutomationElement control)
    {
        try
        {
            if (control.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
                return ((ValuePattern)valuePattern).Current.Value;
            if (control.TryGetCurrentPattern(SelectionPattern.Pattern, out object selectionPattern))
            {
                AutomationElement[] selection = ((SelectionPattern)selectionPattern).Current.GetSelection();
                return selection.Length == 1 ? selection[0].Current.Name : null;
            }
        }
        catch { }
        return null;
    }

    private static void ClearSaveReport(AutomationElement panel, IList<string> diagnostics)
    {
        AutomationElement? checkBox = panel.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox))
            .Cast<AutomationElement>().FirstOrDefault(item => ContainsLabel(item.Current.Name, "Save report"));
        if (checkBox == null) return;

        try
        {
            if (checkBox.TryGetCurrentPattern(TogglePattern.Pattern, out object pattern) &&
                ((TogglePattern)pattern).Current.ToggleState == ToggleState.On)
            {
                ((TogglePattern)pattern).Toggle();
                diagnostics.Add("Save report was cleared.");
            }
        }
        catch (Exception exception)
        {
            diagnostics.Add("Save report could not be changed: " + Short(exception.Message));
        }
    }

    private static AutomationElement? FindButton(AutomationElement panel, string name) =>
        panel.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
            .Cast<AutomationElement>().FirstOrDefault(item =>
                string.Equals(item.Current.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryInvokeButton(AutomationElement panel, string name, IList<string> diagnostics)
    {
        string stage = string.Equals(name, "OK", StringComparison.OrdinalIgnoreCase)
            ? "OK_INVOKED"
            : "CANCEL_INVOKED";
        AutomationElement? button = FindButton(panel, name);
        if (button == null)
        {
            diagnostics.Add(stage + " failed button=" + name + " reason=button_not_found.");
            return false;
        }
        try
        {
            if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
            {
                diagnostics.Add(stage + " failed button=" + name + " reason=InvokePattern_missing supportedPatterns=" + SupportedPatterns(button) + ".");
                return false;
            }
            ((InvokePattern)pattern).Invoke();
            diagnostics.Add(stage + " button=" + name + ".");
            return true;
        }
        catch (Exception exception)
        {
            diagnostics.Add(stage + " failed button=" + name + " reason=Invoke_error detail=" + Short(exception.Message) + ".");
            return false;
        }
    }

    private static string SeenLabelRoles(AutomationElement panel, string label)
    {
        try
        {
            string[] roles = panel.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Where(item => ContainsLabel(item.Current.Name, label))
                .Select(item => item.Current.ControlType.ProgrammaticName)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return roles.Length == 0 ? "none" : string.Join(",", roles);
        }
        catch { return "unavailable"; }
    }

    private static string DescribeControl(string label, AutomationElement? control) =>
        control == null
            ? label + "=missing"
            : label + "=" + control.Current.ControlType.ProgrammaticName +
              " patterns=" + SupportedPatterns(control);

    private static string SupportedPatterns(AutomationElement element)
    {
        try
        {
            AutomationPattern[] patterns = element.GetSupportedPatterns();
            return patterns.Length == 0
                ? "none"
                : string.Join(",", patterns.Select(pattern => pattern.ProgrammaticName));
        }
        catch { return "unavailable"; }
    }

    private static string SafeName(AutomationElement element)
    {
        try { return element.Current.Name; }
        catch { return ""; }
    }

    private static string SafeLocalizedControlType(AutomationElement element)
    {
        try { return element.Current.LocalizedControlType; }
        catch { return ""; }
    }

    private static string SafeControlType(AutomationElement element)
    {
        try { return element.Current.ControlType.ProgrammaticName; }
        catch { return "unavailable"; }
    }

    private static string SafeAutomationId(AutomationElement element)
    {
        try { return element.Current.AutomationId; }
        catch { return ""; }
    }

    private static string SafeClassName(AutomationElement element)
    {
        try { return element.Current.ClassName; }
        catch { return ""; }
    }

    private static string SafeFrameworkId(AutomationElement element)
    {
        try { return element.Current.FrameworkId; }
        catch { return ""; }
    }

    private static int SafeProcessId(AutomationElement element)
    {
        try { return element.Current.ProcessId; }
        catch { return -1; }
    }

    private static bool SafeIsOffscreen(AutomationElement element)
    {
        try { return element.Current.IsOffscreen; }
        catch { return true; }
    }

    private static bool SafeIsEnabled(AutomationElement element)
    {
        try { return element.Current.IsEnabled; }
        catch { return false; }
    }

    private static string SafeBounds(AutomationElement element)
    {
        try { return element.Current.BoundingRectangle.ToString(); }
        catch { return "unavailable"; }
    }

    private static GeometryLabExportPanelSetupResult Failure(
        AutomationElement panel,
        IReadOnlyList<string> diagnostics,
        string message) => new() { Panel = panel, Message = message, Diagnostics = diagnostics };

    private static bool ValuesMatch(string? actual, string expected, bool pathValue)
    {
        if (string.IsNullOrWhiteSpace(actual)) return false;
        if (!pathValue)
            return string.Equals(actual.Trim(), expected, StringComparison.OrdinalIgnoreCase);
        try
        {
            return string.Equals(Path.GetFullPath(actual.Trim()), Path.GetFullPath(expected),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool ContainsLabel(string? name, string expected) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string Short(string text) =>
        string.IsNullOrWhiteSpace(text) ? "unknown" : text.Length <= 180 ? text : text.Substring(0, 180);

    private static string ValueOrEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? "(empty)" : value;

    private static string ValueOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? "(null_or_empty)" : value;

    private sealed record PanelCandidateEvidence(bool IsAcceptable, string Diagnostic);

    private sealed class StructuralCandidate
    {
        internal StructuralCandidate(AutomationElement element)
        {
            Element = element;
            CommonContainer = element;
        }
        internal AutomationElement Element { get; }
        internal AutomationElement CommonContainer { get; private set; }
        internal HashSet<string> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, AutomationElement> LabelElements { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        internal void AddLabel(string label, AutomationElement source)
        {
            Labels.Add(label);
            if (!LabelElements.ContainsKey(label)) LabelElements[label] = source;
        }

        internal StructuralCandidate WithNearestCommonContainer()
        {
            if (LabelElements.Count < 2) return this;
            AutomationElement first = LabelElements.Values.First();
            foreach (AutomationElement ancestor in Ancestors(first))
            {
                if (LabelElements.Values.All(element => Ancestors(element).Any(item => item.Equals(ancestor))))
                {
                    CommonContainer = ancestor;
                    break;
                }
            }
            return this;
        }

        private static IEnumerable<AutomationElement> Ancestors(AutomationElement element)
        {
            for (AutomationElement? current = element; current != null;
                 current = TreeWalker.ControlViewWalker.GetParent(current))
                yield return current;
        }
    }
}
