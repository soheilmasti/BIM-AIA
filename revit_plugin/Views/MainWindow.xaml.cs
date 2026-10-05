using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using BIM_AIA.Handlers;
using BIM_AIA.Services;

namespace BIM_AIA.Views
{
    public class RoomScopeOption
    {
        public ElementId? RoomId { get; set; }
        public string DisplayName { get; set; } = "";
        public string ScopeType { get; set; } = "All"; // "All", "Selected", "Room"
    }

    public partial class MainWindow : Window
    {
        private readonly UIDocument _uidoc;
        private readonly ExternalEvent _externalEvent;
        private readonly BIMAIATransactionHandler _handler;
        private readonly AiService _aiService;
        private string? _pendingSuggestedActionTitle;
        private string? _pendingSuggestedActionTool;
        private MepPlanReport? _lastMepReport;

        public MainWindow(UIDocument uidoc, ExternalEvent externalEvent, BIMAIATransactionHandler handler)
        {
            InitializeComponent();
            _uidoc = uidoc;
            _externalEvent = externalEvent;
            _handler = handler;
            _aiService = new AiService();

            // Connect AI Self-Healing logs
            RevitSelfHealingEngine.OnSelfHealedLog = (msg) =>
            {
                Dispatcher.Invoke(() => AppendLog(msg));
            };

            _handler.OnExecutionFinished = (success, msg) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!success)
                    {
                        AppendLog(msg);
                    }
                    RefreshChangesGrid();
                });
            };

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateLiveDocumentInfo();
            PopulateLevelDropdown();
            RefreshChangesGrid();

            AppendLog("✅ BIM AIA Copilot connected to active Autodesk Revit 2026 model.");
            AppendLog("🛡️ AI Self-Healing Engine initialized: Automatic error recovery & resilience active.");
            AppendLog("👉 Select Level, Discipline & Space above, or type natural design prompts in the prompt box.");
        }

        private void PopulateLevelDropdown()
        {
            try
            {
                Document doc = _uidoc.Document;
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                CmbTargetLevel.ItemsSource = levels;

                if (levels.Count > 0)
                {
                    // Select level of active view if applicable
                    Level? activeLevel = (doc.ActiveView is ViewPlan vp && vp.GenLevel != null) 
                        ? vp.GenLevel 
                        : levels.FirstOrDefault();

                    CmbTargetLevel.SelectedItem = activeLevel ?? levels.First();
                }
            }
            catch { }
        }

        private void CmbTargetLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PopulateRoomScopeDropdown();
        }

        private void PopulateRoomScopeDropdown()
        {
            try
            {
                Document doc = _uidoc.Document;
                Level? selectedLevel = CmbTargetLevel.SelectedItem as Level;
                if (selectedLevel == null) return;

                var options = new List<RoomScopeOption>
                {
                    new RoomScopeOption { DisplayName = "🏢 Entire Level (All Spaces)", ScopeType = "All" },
                    new RoomScopeOption { DisplayName = "🎯 Selected in Revit View", ScopeType = "Selected" }
                };

                var rooms = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement))
                    .WhereElementIsNotElementType()
                    .OfType<Room>()
                    .Where(r => r.LevelId == selectedLevel.Id && r.Area > 1.0)
                    .OrderBy(r => r.Number)
                    .ToList();

                foreach (var r in rooms)
                {
                    string name = string.IsNullOrWhiteSpace(r.Name) ? "Room" : r.Name;
                    string num = string.IsNullOrWhiteSpace(r.Number) ? "" : r.Number;
                    string lower = name.ToLower();
                    string icon = (lower.Contains("toilet") || lower.Contains("bath") || lower.Contains("wc") || lower.Contains("سرویس") || lower.Contains("توالت") || lower.Contains("دستشویی")) 
                        ? "🚽 " 
                        : (lower.Contains("corridor") || lower.Contains("hall") || lower.Contains("راهرو") ? "🚶 " : "🏢 ");

                    options.Add(new RoomScopeOption
                    {
                        RoomId = r.Id,
                        DisplayName = $"{icon}{num} - {name}",
                        ScopeType = "Room"
                    });
                }

                CmbRoomScope.ItemsSource = options;
                CmbRoomScope.SelectedIndex = 0;
            }
            catch { }
        }

        private void UpdateLiveDocumentInfo()
        {
            try
            {
                Document doc = _uidoc.Document;
                string docTitle = string.IsNullOrWhiteSpace(doc.Title) ? "Active Model" : doc.Title;
                string viewName = doc.ActiveView != null ? doc.ActiveView.Name : "Default View";
                int selCount = _uidoc.Selection.GetElementIds().Count;

                TxtProjectHeader.Text = $"Project: {docTitle}";
                TxtViewHeader.Text = $"Active View: {viewName} | Selected: {selCount}";
            }
            catch { }
        }

        private void RefreshChangesGrid()
        {
            try
            {
                RevitActionExecutor.ScanDocumentForChanges(_uidoc.Document);
                var list = RevitActionExecutor.ChangeHistory.ToList();
                GridChanges.ItemsSource = null;
                GridChanges.ItemsSource = list;
                TxtChangesCount.Text = $"Items: {list.Count}";
            }
            catch { }
        }

        private void BtnRescan_Click(object sender, RoutedEventArgs e)
        {
            UpdateLiveDocumentInfo();
            RefreshChangesGrid();
            AppendLog("🔄 Model scanned: Elements and change log refreshed.");
        }

        private void BtnInspectSelected_Click(object sender, RoutedEventArgs e)
        {
            NavigateToSelectedElement();
        }

        private void GridChanges_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            NavigateToSelectedElement();
        }

        private void NavigateToSelectedElement()
        {
            var selected = GridChanges.SelectedItem as ModelChangeRecord;
            if (selected == null || selected.ElementId == ElementId.InvalidElementId)
            {
                // If nothing selected, pick the first available item in the grid
                if (GridChanges.Items.Count > 0)
                {
                    GridChanges.SelectedIndex = 0;
                    selected = GridChanges.SelectedItem as ModelChangeRecord;
                }
            }

            if (selected == null || selected.ElementId == ElementId.InvalidElementId)
            {
                TaskDialog.Show("BIM AIA", "Please select an item from the table to inspect.");
                return;
            }

            AppendLog($"🎯 Focusing camera on Element ID {selected.ElementIdString} in 3D audit view...");

            _handler.TransactionTitle = $"BIM AIA: Inspect {selected.ChangeType}";
            _handler.ActionToExecute = (uiapp) =>
            {
                RevitActionExecutor.NavigateToVisualAuditView(_uidoc, selected.ElementId);
            };

            _externalEvent.Raise();
        }

        private void TxtPromptInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnSendPrompt_Click(sender, e);
            }
        }

        private async void BtnSendPrompt_Click(object sender, RoutedEventArgs e)
        {
            string prompt = TxtPromptInput.Text.Trim();
            if (string.IsNullOrEmpty(prompt))
            {
                AppendLog("⚠️ Please type a command or question in the prompt box.");
                return;
            }

            TxtPromptInput.Text = "";
            UpdateLiveDocumentInfo();

            if (RadioConsult.IsChecked == true)
            {
                await HandleConsultModeAsync(prompt);
            }
            else
            {
                HandleExecuteMode(prompt);
            }
        }

        private async Task HandleConsultModeAsync(string prompt)
        {
            AppendLog($"🔍 [Technical Consultation]: '{prompt}'");
            try
            {
                var selIds = _uidoc.Selection.GetElementIds().Select(id => id.Value.ToString()).ToList();
                string docTitle = _uidoc.Document.Title;
                string viewName = _uidoc.Document.ActiveView.Name;

                var result = await _aiService.ProcessQueryAsync(prompt, "consult", docTitle, viewName, selIds);

                AppendLog($"💡 [Consultant Response - {result.ModelUsed}]:\n{result.Response}\n");

                if (result.HasSuggestedAction)
                {
                    _pendingSuggestedActionTitle = result.ActionTitle;
                    _pendingSuggestedActionTool = result.ActionTool;

                    BtnApplySuggestedAction.Content = $"⚡ Apply: {_pendingSuggestedActionTitle}";
                    BtnApplySuggestedAction.Visibility = System.Windows.Visibility.Visible;
                    AppendLog($"👉 Engineering action available. Click the pink button above to execute directly in the model.");
                }
                else
                {
                    BtnApplySuggestedAction.Visibility = System.Windows.Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"💡 [Offline Consultant]: {ex.Message}");
            }
        }

        private void BtnRunGenerativeMep_Click(object sender, RoutedEventArgs e)
        {
            Level? targetLevel = CmbTargetLevel.SelectedItem as Level;
            MepDiscipline discipline = (MepDiscipline)CmbDiscipline.SelectedIndex;
            RoomScopeOption? scope = CmbRoomScope.SelectedItem as RoomScopeOption;

            string? roomFilter = null;
            ICollection<ElementId>? specificIds = null;

            if (scope != null)
            {
                if (scope.ScopeType == "Selected")
                {
                    specificIds = _uidoc.Selection.GetElementIds();
                }
                else if (scope.ScopeType == "Room" && scope.RoomId != null)
                {
                    specificIds = new List<ElementId> { scope.RoomId };
                    roomFilter = scope.DisplayName;
                }
            }

            bool isDirect = RadioExecute.IsChecked == true;
            ExecuteGenerativeMep(targetLevel?.Id, targetLevel?.Name, discipline, roomFilter, specificIds, isDirect);
        }

        private void HandleExecuteMode(string prompt)
        {
            string pLower = prompt.ToLower();

            // Intercept MEP generative design requests in natural language
            if (pLower.Contains("طراحی") || pLower.Contains("تاسیسات") || pLower.Contains("mep") ||
                pLower.Contains("electr") || pLower.Contains("cable") || pLower.Contains("tray") ||
                pLower.Contains("light") || pLower.Contains("socket") || pLower.Contains("power") ||
                pLower.Contains("الکتریک") || pLower.Contains("برق") || pLower.Contains("روشنایی") ||
                pLower.Contains("پریز") || pLower.Contains("کابل") ||
                pLower.Contains("mech") || pLower.Contains("hvac") || pLower.Contains("duct") ||
                pLower.Contains("کانال") || pLower.Contains("داکت") || pLower.Contains("تهویه") ||
                pLower.Contains("plumb") || pLower.Contains("pipe") || pLower.Contains("drain") ||
                pLower.Contains("فاضلاب") || pLower.Contains("لوله") || pLower.Contains("آبرسانی") ||
                pLower.Contains("دستشویی") || pLower.Contains("سرویس") || pLower.Contains("توالت") ||
                pLower.Contains("toilet") || pLower.Contains("restroom") || pLower.Contains("wc"))
            {
                bool isDirectExecution = pLower.Contains("اجرا") || pLower.Contains("ترسیم") || pLower.Contains("بکش") || 
                                         pLower.Contains("بساز") || pLower.Contains("مدل") || pLower.Contains("create") || 
                                         pLower.Contains("model") || pLower.Contains("execute") || pLower.Contains("draw") || 
                                         pLower.Contains("build") || pLower.Contains("run");

                ParseMepParametersFromPrompt(prompt, out ElementId? parsedLevelId, out string? parsedLevelHint, out MepDiscipline discipline, out string? roomFilter);
                ExecuteGenerativeMep(parsedLevelId, parsedLevelHint, discipline, roomFilter, null, isDirectExecution);
                return;
            }

            AppendLog($"⚡ Sending CAD execution command: '{prompt}'");

            _handler.TransactionTitle = "BIM AIA: " + prompt;
            _handler.ActionToExecute = (uiapp) =>
            {
                ExecutionResult res;
                if (pLower.Contains("view") || pLower.Contains("show") || pLower.Contains("audit") || pLower.Contains("highlight") ||
                    pLower.Contains("clash") || pLower.Contains("inspect") || pLower.Contains("3d") || pLower.Contains("display") ||
                    pLower.Contains("isolate") || pLower.Contains("focus") ||
                    pLower.Contains("نمایش") || pLower.Contains("دید") || pLower.Contains("ویو") || pLower.Contains("ببین") ||
                    pLower.Contains("نشان") || pLower.Contains("تصویر") || pLower.Contains("هایلایت"))
                {
                    res = RevitActionExecutor.ExecuteVisualAudit(_uidoc);
                }
                else if (pLower.Contains("reroute") || pLower.Contains("duct") || pLower.Contains("offset") || pLower.Contains("move") || pLower.Contains("bypass") ||
                         pLower.Contains("داکت") || pLower.Contains("جابجا") || pLower.Contains("دور") || pLower.Contains("تغییر مسیر"))
                {
                    res = RevitActionExecutor.ExecuteRerouteOrOffset(_uidoc, 300.0);
                }
                else if (pLower.Contains("slope") || pLower.Contains("pipe") || pLower.Contains("drainage") ||
                         pLower.Contains("شیب") || pLower.Contains("لوله") || pLower.Contains("فاضلاب"))
                {
                    res = RevitActionExecutor.ExecuteSetSlope(_uidoc, 0.015);
                }
                else if (pLower.Contains("sleeve") || pLower.Contains("void") || pLower.Contains("penetration") ||
                         pLower.Contains("اسلیو") || pLower.Contains("غلاف") || pLower.Contains("بازشو"))
                {
                    res = RevitActionExecutor.ExecuteVisualAudit(_uidoc);
                }
                else if (pLower.Contains("uniclass") || pLower.Contains("parameter") || pLower.Contains("code") || pLower.Contains("comment") ||
                         pLower.Contains("پارامتر") || pLower.Contains("کد"))
                {
                    res = RevitActionExecutor.ExecuteBatchParameterUpdate(_uidoc, "UniclassPr_Code", "Pr_65_52_63");
                }
                else
                {
                    res = RevitActionExecutor.ExecuteVisualAudit(_uidoc);
                }

                Dispatcher.Invoke(() =>
                {
                    if (res.Success)
                    {
                        AppendLog($"✅ {res.Message}");
                    }
                    else
                    {
                        AppendLog($"⚠️ {res.Message}");
                    }
                    RefreshChangesGrid();
                });
            };

            _externalEvent.Raise();
        }

        private void ParseMepParametersFromPrompt(string prompt, out ElementId? levelId, out string? levelHint, out MepDiscipline discipline, out string? roomFilter)
        {
            levelId = null;
            levelHint = null;
            roomFilter = null;
            discipline = MepDiscipline.FullMep;

            string p = prompt.ToLower();

            // 1. Level Detection
            if (p.Contains("طبقه 1") || p.Contains("طبقه اول") || p.Contains("level 1") || p.Contains("first floor") || p.Contains("همکف"))
            {
                levelHint = "Level 1";
            }
            else if (p.Contains("طبقه 2") || p.Contains("طبقه دوم") || p.Contains("level 2") || p.Contains("second floor"))
            {
                levelHint = "Level 2";
            }
            else if (p.Contains("طبقه 3") || p.Contains("طبقه سوم") || p.Contains("level 3") || p.Contains("third floor"))
            {
                levelHint = "Level 3";
            }
            else if (p.Contains("طبقه 4") || p.Contains("طبقه چهارم") || p.Contains("level 4"))
            {
                levelHint = "Level 4";
            }
            else if (p.Contains("بام") || p.Contains("roof"))
            {
                levelHint = "Roof";
            }
            else if (CmbTargetLevel.SelectedItem is Level selLvl)
            {
                levelId = selLvl.Id;
                levelHint = selLvl.Name;
            }

            // 2. Discipline Detection
            bool isMech = p.Contains("مکانیک") || p.Contains("hvac") || p.Contains("تهویه") || p.Contains("داکت") || p.Contains("کانال");
            bool isPlumb = p.Contains("لوله") || p.Contains("فاضلاب") || p.Contains("شیب") || p.Contains("آبرسانی") || p.Contains("drain") || p.Contains("plumb");
            bool isElec = p.Contains("الکتریک") || p.Contains("برق") || p.Contains("روشنایی") || p.Contains("پریز") || p.Contains("سینی کابل") || p.Contains("چراغ") || p.Contains("light") || p.Contains("socket") || p.Contains("cable");

            if (isMech && !isElec && !isPlumb) discipline = MepDiscipline.MechanicalHvac;
            else if (isPlumb && !isElec && !isMech) discipline = MepDiscipline.PlumbingSanitary;
            else if (isElec && !isMech && !isPlumb) discipline = MepDiscipline.Electrical;
            else discipline = MepDiscipline.FullMep;

            // 3. Room / Space Targeting
            if (p.Contains("دستشویی") || p.Contains("توالت") || p.Contains("سرویس") || p.Contains("toilet") || p.Contains("restroom") || p.Contains("wc"))
            {
                roomFilter = "toilet";
                if (!isElec && !isMech && !isPlumb) discipline = MepDiscipline.FullMep;
            }
            else if (p.Contains("راهرو") || p.Contains("corridor") || p.Contains("hall"))
            {
                roomFilter = "corridor";
            }
            else if (p.Contains("اتاق") || p.Contains("office") || p.Contains("جلسات"))
            {
                roomFilter = "office";
            }

            // Match room numbers e.g. "102", "12", "01"
            var digitMatches = Regex.Matches(p, @"\b\d{1,4}\b");
            foreach (Match dm in digitMatches)
            {
                if (levelHint != null && levelHint.Contains(dm.Value)) continue;
                roomFilter = dm.Value;
                break;
            }
        }

        private void ExecuteGenerativeMep(ElementId? levelId, string? levelHint, MepDiscipline discipline, string? roomFilter, ICollection<ElementId>? selectedIds, bool isDirectExecution = false)
        {
            string targetScopeText = string.IsNullOrWhiteSpace(roomFilter) ? "Entire Level" : $"Target Space: '{roomFilter}'";
            string modeText = isDirectExecution ? "Direct 3D Modeling & Execution" : "Engineering Analysis & Blueprint";
            AppendLog($"⚡ Initiating Generative MEP [{discipline}, {levelHint ?? "Model"}, {targetScopeText}, Mode: {modeText}]...");

            _handler.TransactionTitle = $"BIM AIA: Generative MEP ({discipline})";
            _handler.ActionToExecute = (uiapp) =>
            {
                try
                {
                    Document doc = _uidoc.Document;
                    var report = GenerativeMepEngine.AnalyzeMep(doc, levelId, levelHint, discipline, roomFilter, selectedIds);
                    _lastMepReport = report;

                    string markdown = GenerativeMepEngine.FormatReportMarkdown(report);

                    // Populate changes table with spatial requirements
                    foreach (var room in report.RoomRequirements)
                    {
                        string desc = discipline switch
                        {
                            MepDiscipline.MechanicalHvac => $"{room.RequiredAirflowCfm:0} CFM Airflow | {room.DuctSizeRecommendation} Duct | {room.CalculatedDiffusers}x Diffusers",
                            MepDiscipline.PlumbingSanitary => $"{room.DrainageFixtureUnits} DFU | {room.PipeSizeRecommendation} | {room.StandardDrainSlopePercent:0.1}% Gravity Slope",
                            MepDiscipline.Electrical => $"{room.CalculatedFixtures}x Fixtures ({room.TargetLux} lx) | {room.CalculatedSockets}x Sockets | {room.EstimatedPowerKva} kVA",
                            _ => $"{room.CalculatedFixtures}x Fixtures | {room.RequiredAirflowCfm:0} CFM Duct | {room.PipeSizeRecommendation}"
                        };

                        string color = discipline switch
                        {
                            MepDiscipline.MechanicalHvac => "#00E5FF",
                            MepDiscipline.PlumbingSanitary => "#10B981",
                            _ => "#FACC15"
                        };

                        RevitActionExecutor.ChangeHistory.Insert(0, new ModelChangeRecord
                        {
                            ElementId = room.RoomId,
                            Category = $"{discipline} Plan",
                            ChangeType = $"{room.RoomName} ({room.SpaceType})",
                            Description = desc,
                            BeforeValue = "Unplanned",
                            AfterValue = "Engineered",
                            ColorHex = color
                        });
                    }

                    // Highlight room zones in 3D viewport safely within a transaction
                    var roomIds = report.RoomRequirements
                        .Where(r => r.RoomId != ElementId.InvalidElementId)
                        .Select(r => r.RoomId)
                        .ToList();

                    if (roomIds.Count > 0)
                    {
                        RevitActionExecutor.EnsureModelView(_uidoc);
                        RevitActionExecutor.SafeFocusElements(_uidoc, roomIds);
                        Color hlColor = discipline == MepDiscipline.MechanicalHvac 
                            ? new Color(0, 229, 255) 
                            : (discipline == MepDiscipline.PlumbingSanitary ? new Color(16, 185, 129) : new Color(250, 204, 21));
                        RevitActionExecutor.ApplyGraphicHighlight(doc, _uidoc.ActiveView, roomIds, hlColor, transparency: 20);
                    }

                    Dispatcher.Invoke(() => AppendLog(markdown));

                    if (isDirectExecution)
                    {
                        var createdIds = new List<ElementId>();

                        if (discipline == MepDiscipline.Electrical || discipline == MepDiscipline.FullMep)
                        {
                            var rTray = GenerativeMepEngine.GenerateCableTraySpine(_uidoc, report);
                            if (rTray.Success && rTray.AffectedElementIds != null)
                            {
                                createdIds.AddRange(rTray.AffectedElementIds);
                            }
                            else if (!rTray.Success)
                            {
                                Dispatcher.Invoke(() => AppendLog($"ℹ️ Cable Tray: {rTray.Message}"));
                            }
                        }

                        if (discipline == MepDiscipline.MechanicalHvac || discipline == MepDiscipline.FullMep)
                        {
                            var rDuct = GenerativeMepEngine.GenerateHvacDuctSpine(_uidoc, report);
                            if (rDuct.Success && rDuct.AffectedElementIds != null)
                            {
                                createdIds.AddRange(rDuct.AffectedElementIds);
                            }
                            else if (!rDuct.Success)
                            {
                                Dispatcher.Invoke(() => AppendLog($"ℹ️ HVAC Duct: {rDuct.Message}"));
                            }
                        }

                        if (discipline == MepDiscipline.PlumbingSanitary || discipline == MepDiscipline.FullMep)
                        {
                            var rPipe = GenerativeMepEngine.GeneratePlumbingDrainPipe(_uidoc, report);
                            if (rPipe.Success && rPipe.AffectedElementIds != null)
                            {
                                createdIds.AddRange(rPipe.AffectedElementIds);
                            }
                            else if (!rPipe.Success)
                            {
                                Dispatcher.Invoke(() => AppendLog($"ℹ️ Plumbing Pipe: {rPipe.Message}"));
                            }
                        }

                        // Closed-loop AI database verification
                        if (createdIds.Count > 0)
                        {
                            var audit = RevitActionExecutor.AuditCreatedElements(doc, createdIds);
                            Dispatcher.Invoke(() =>
                            {
                                AppendLog($"✅ **AI Closed-Loop Audit:** {audit.SummaryText}");
                                RefreshChangesGrid();
                            });
                        }
                        else
                        {
                            Dispatcher.Invoke(() =>
                            {
                                AppendLog("ℹ️ No 3D geometry was generated due to missing model prerequisites detailed above.");
                                RefreshChangesGrid();
                            });
                        }
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            RefreshChangesGrid();

                            if (discipline == MepDiscipline.MechanicalHvac)
                            {
                                _pendingSuggestedActionTool = "tool_generate_hvac_duct";
                                _pendingSuggestedActionTitle = $"Generate 3D HVAC Duct ({report.LevelName})";
                                BtnApplySuggestedAction.Content = $"❄️ Generate 3D HVAC Duct ({report.LevelName})";
                            }
                            else if (discipline == MepDiscipline.PlumbingSanitary)
                            {
                                _pendingSuggestedActionTool = "tool_generate_plumbing_pipe";
                                _pendingSuggestedActionTitle = $"Generate 3D Sloped Sanitary Pipe ({report.LevelName})";
                                BtnApplySuggestedAction.Content = $"🚰 Generate 3D Sloped Pipe ({report.LevelName})";
                            }
                            else if (discipline == MepDiscipline.FullMep)
                            {
                                _pendingSuggestedActionTool = "tool_generate_all_mep";
                                _pendingSuggestedActionTitle = $"Generate Multi-Discipline MEP Systems ({report.LevelName})";
                                BtnApplySuggestedAction.Content = $"🌐 Generate 3D MEP Systems ({report.LevelName})";
                            }
                            else
                            {
                                _pendingSuggestedActionTool = "tool_generate_cable_tray";
                                _pendingSuggestedActionTitle = $"Generate 3D Cable Tray Backbone ({report.LevelName})";
                                BtnApplySuggestedAction.Content = $"⚡ Generate Cable Tray ({report.LevelName})";
                            }

                            BtnApplySuggestedAction.Visibility = System.Windows.Visibility.Visible;
                            AppendLog($"👉 Action ready! Click '{BtnApplySuggestedAction.Content}' to model real 3D geometry in Revit.");
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendLog($"⚠️ Error analyzing MEP: {ex.Message}"));
                }
            };

            _externalEvent.Raise();
        }

        private void BtnApplySuggestedAction_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pendingSuggestedActionTool)) return;
            string tool = _pendingSuggestedActionTool;
            string title = _pendingSuggestedActionTitle ?? "Execute AI Decision";

            BtnApplySuggestedAction.Visibility = System.Windows.Visibility.Collapsed;
            AppendLog($"⚡ Executing AI recommendation: '{title}'");

            _handler.TransactionTitle = "BIM AIA: " + title;
            _handler.ActionToExecute = (uiapp) =>
            {
                ExecutionResult res = new ExecutionResult();

                if (tool == "tool_generate_cable_tray")
                {
                    if (_lastMepReport == null)
                    {
                        _lastMepReport = GenerativeMepEngine.AnalyzeMep(_uidoc.Document, null, null, MepDiscipline.Electrical, null, null);
                    }
                    res = GenerativeMepEngine.GenerateCableTraySpine(_uidoc, _lastMepReport);

                    if (res.Success)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _pendingSuggestedActionTool = "tool_place_mep_fixtures";
                            _pendingSuggestedActionTitle = $"Place Lighting Fixture Array ({_lastMepReport.LevelName})";
                            BtnApplySuggestedAction.Content = "💡 Place Lighting Fixtures on Grid";
                            BtnApplySuggestedAction.Visibility = System.Windows.Visibility.Visible;
                            AppendLog("💡 Cable Tray created! You can now click above to place lighting fixtures if families are loaded.");
                        });
                    }
                }
                else if (tool == "tool_generate_hvac_duct")
                {
                    if (_lastMepReport == null)
                    {
                        _lastMepReport = GenerativeMepEngine.AnalyzeMep(_uidoc.Document, null, null, MepDiscipline.MechanicalHvac, null, null);
                    }
                    res = GenerativeMepEngine.GenerateHvacDuctSpine(_uidoc, _lastMepReport);
                }
                else if (tool == "tool_generate_plumbing_pipe")
                {
                    if (_lastMepReport == null)
                    {
                        _lastMepReport = GenerativeMepEngine.AnalyzeMep(_uidoc.Document, null, null, MepDiscipline.PlumbingSanitary, null, null);
                    }
                    res = GenerativeMepEngine.GeneratePlumbingDrainPipe(_uidoc, _lastMepReport);
                }
                else if (tool == "tool_generate_all_mep")
                {
                    if (_lastMepReport == null)
                    {
                        _lastMepReport = GenerativeMepEngine.AnalyzeMep(_uidoc.Document, null, null, MepDiscipline.FullMep, null, null);
                    }
                    var r1 = GenerativeMepEngine.GenerateCableTraySpine(_uidoc, _lastMepReport);
                    var r2 = GenerativeMepEngine.GenerateHvacDuctSpine(_uidoc, _lastMepReport);
                    var r3 = GenerativeMepEngine.GeneratePlumbingDrainPipe(_uidoc, _lastMepReport);
                    res = new ExecutionResult
                    {
                        Success = r1.Success || r2.Success || r3.Success,
                        Message = $"MEP Spines Generated: Cable Tray: {(r1.Success ? "OK" : "Skipped")}, Duct: {(r2.Success ? "OK" : "Skipped")}, Sloped Pipe: {(r3.Success ? "OK" : "Skipped")}"
                    };
                }
                else if (tool == "tool_place_mep_fixtures")
                {
                    if (_lastMepReport == null)
                    {
                        _lastMepReport = GenerativeMepEngine.AnalyzeMep(_uidoc.Document, null, null, MepDiscipline.FullMep, null, null);
                    }
                    res = GenerativeMepEngine.PlaceAvailableFixtures(_uidoc, _lastMepReport);
                }
                else if (tool == "tool_reroute_duct")
                {
                    res = RevitActionExecutor.ExecuteRerouteOrOffset(_uidoc, 300.0);
                }
                else if (tool == "tool_set_pipe_slope")
                {
                    res = RevitActionExecutor.ExecuteSetSlope(_uidoc, 0.015);
                }
                else
                {
                    res = RevitActionExecutor.ExecuteBatchParameterUpdate(_uidoc, "Comments", title);
                }

                Dispatcher.Invoke(() =>
                {
                    if (res.Success)
                    {
                        AppendLog($"✅ {res.Message}");
                        if (res.AffectedElementIds != null && res.AffectedElementIds.Count > 0)
                        {
                            var audit = RevitActionExecutor.AuditCreatedElements(_uidoc.Document, res.AffectedElementIds);
                            AppendLog($"📋 **AI Closed-Loop Audit:** {audit.SummaryText}");
                        }
                    }
                    else
                    {
                        AppendLog($"⚠️ {res.Message}");
                    }
                    RefreshChangesGrid();
                });
            };

            _externalEvent.Raise();
        }

        // Quick Actions
        private void QuickAction_Reroute_Click(object sender, RoutedEventArgs e)
        {
            HandleExecuteMode("Offset and reroute element -300mm to bypass spatial clash");
        }

        private void QuickAction_Slope_Click(object sender, RoutedEventArgs e)
        {
            HandleExecuteMode("Set 1.5% standard slope for gravity drainage pipes");
        }

        private void QuickAction_Uniclass_Click(object sender, RoutedEventArgs e)
        {
            HandleExecuteMode("Inject standard Uniclass classification code to elements");
        }

        private void QuickAction_Audit_Click(object sender, RoutedEventArgs e)
        {
            HandleExecuteMode("Audit and highlight viewport elements for clash review");
        }

        private void AppendLog(string message)
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            TxtConsoleLog.AppendText($"[{time}] {message}\n");
            TxtConsoleLog.ScrollToEnd();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
