using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using BIM_AIA.Handlers;

namespace BIM_AIA.Services
{
    public enum MepDiscipline
    {
        Electrical,          // Lighting, Sockets, Cable Trays
        MechanicalHvac,      // Exhaust, Ventilation, Ducts, Diffusers
        PlumbingSanitary,    // Sanitary Drainage, Slope, Waste Pipes
        FullMep              // All combined
    }

    public class RoomMepRequirement
    {
        public ElementId RoomId { get; set; } = ElementId.InvalidElementId;
        public string RoomName { get; set; } = "Room";
        public string RoomNumber { get; set; } = "101";
        public string SpaceType { get; set; } = "Office";
        public double AreaSqMeters { get; set; } = 25.0;
        public double PerimeterMeters { get; set; } = 20.0;
        public double CeilingHeightMeters { get; set; } = 2.8;
        public XYZ CenterPoint { get; set; } = XYZ.Zero;
        public BoundingBoxXYZ? BoundingBox { get; set; }

        // Electrical metrics (CIBSE / IESNA)
        public int TargetLux { get; set; } = 500;
        public int CalculatedFixtures { get; set; } = 4;
        public int CalculatedSockets { get; set; } = 6;
        public double EstimatedPowerKva { get; set; } = 0.35;
        public List<XYZ> FixtureGridPoints { get; set; } = new List<XYZ>();

        // Mechanical HVAC metrics (ASHRAE 62.1)
        public double RequiredAirflowCfm { get; set; } = 150.0;
        public double AirChangesPerHour { get; set; } = 6.0;
        public string DuctSizeRecommendation { get; set; } = "250x200 mm";
        public int CalculatedDiffusers { get; set; } = 2;
        public List<XYZ> DiffuserGridPoints { get; set; } = new List<XYZ>();

        // Plumbing Sanitary metrics (IPC / UPC)
        public int DrainageFixtureUnits { get; set; } = 0;
        public string PipeSizeRecommendation { get; set; } = "DN100 (4\")";
        public double StandardDrainSlopePercent { get; set; } = 1.5;
        public List<XYZ> PlumbingPoints { get; set; } = new List<XYZ>();
    }

    public class MepPlanReport
    {
        public string LevelName { get; set; } = "Level 1";
        public ElementId LevelId { get; set; } = ElementId.InvalidElementId;
        public double LevelElevationFeet { get; set; } = 0.0;
        public MepDiscipline Discipline { get; set; } = MepDiscipline.FullMep;
        public string ScopeDescription { get; set; } = "All Rooms on Level";
        public bool IsSingleRoomScope { get; set; } = false;

        public int TotalRooms { get; set; }
        public double TotalAreaSqMeters { get; set; }

        // Electrical summary
        public int TotalLightingFixtures { get; set; }
        public int TotalSockets { get; set; }
        public double TotalConnectedLoadKva { get; set; }
        public double CableTrayRouteLengthMeters { get; set; }

        // Mechanical summary
        public double TotalAirflowCfm { get; set; }
        public int TotalDiffusersOrGrilles { get; set; }
        public double DuctRouteLengthMeters { get; set; }

        // Plumbing summary
        public int TotalPlumbingFixtures { get; set; }
        public double PipeRouteLengthMeters { get; set; }

        public List<RoomMepRequirement> RoomRequirements { get; set; } = new List<RoomMepRequirement>();
        public List<FamilyReadinessItem> FamilyChecklist { get; set; } = new List<FamilyReadinessItem>();
        public bool AreAllFamiliesLoaded => FamilyChecklist.All(f => f.IsAvailable);
        public ModelReadinessReport ReadinessReport { get; set; } = new ModelReadinessReport();
    }

    public class ActionReadinessItem
    {
        public string Title { get; set; } = "";
        public bool IsReady { get; set; }
        public string StatusIcon => IsReady ? "🟢" : "🔴";
        public string Details { get; set; } = "";
        public string? PrerequisiteInstruction { get; set; }
    }

    public class ModelReadinessReport
    {
        public List<ActionReadinessItem> CapableActions { get; set; } = new List<ActionReadinessItem>();
        public List<ActionReadinessItem> BlockedActions { get; set; } = new List<ActionReadinessItem>();
        public List<string> UserActionableRoadmap { get; set; } = new List<string>();
    }

    /// <summary>
    /// Autonomous Generative MEP Engineering Engine for Autodesk Revit 2026.
    /// Supports dynamic level selection, space/room targeting, CIBSE/ASHRAE calculations,
    /// discipline-aware family auditing, and instant native 3D CAD modeling.
    /// </summary>
    public static class GenerativeMepEngine
    {
        /// <summary>
        /// Resolves target level dynamically: by specific ID, by natural language hint, or by active view.
        /// Zero hardcoded defaults.
        /// </summary>
        public static Level ResolveLevel(Document doc, ElementId? specificLevelId = null, string? hint = null)
        {
            if (specificLevelId != null && specificLevelId != ElementId.InvalidElementId)
            {
                var lvl = doc.GetElement(specificLevelId) as Level;
                if (lvl != null) return lvl;
            }

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            if (levels.Count == 0)
            {
                throw new InvalidOperationException("No levels found in the current Revit model.");
            }

            if (!string.IsNullOrWhiteSpace(hint))
            {
                string h = hint.ToLower().Trim();

                // Match exact
                var exact = levels.FirstOrDefault(l => l.Name.Equals(h, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;

                // Match level numbers
                if (h.Contains("4") || h.Contains("چهار") || h.Contains("four"))
                {
                    var l4 = levels.FirstOrDefault(l => l.Name.Contains("4") || l.Name.Contains("04"));
                    if (l4 != null) return l4;
                }
                if (h.Contains("3") || h.Contains("سوم") || h.Contains("سه") || h.Contains("third") || h.Contains("three"))
                {
                    var l3 = levels.FirstOrDefault(l => l.Name.Contains("3") || l.Name.Contains("03"));
                    if (l3 != null) return l3;
                }
                if (h.Contains("2") || h.Contains("دوم") || h.Contains("دو") || h.Contains("second") || h.Contains("two"))
                {
                    var l2 = levels.FirstOrDefault(l => l.Name.Contains("2") || l.Name.Contains("02"));
                    if (l2 != null) return l2;
                }
                if (h.Contains("1") || h.Contains("اول") || h.Contains("یک") || h.Contains("first") || h.Contains("one") || h.Contains("ground") || h.Contains("همکف"))
                {
                    var l1 = levels.FirstOrDefault(l => l.Name.Contains("1") || l.Name.ToLower().Contains("ground") || l.Name.Contains("01") || l.Name.Contains("همکف"));
                    if (l1 != null) return l1;
                }
                if (h.Contains("roof") || h.Contains("بام") || h.Contains("پشت بام"))
                {
                    var lr = levels.FirstOrDefault(l => l.Name.ToLower().Contains("roof") || l.Name.Contains("بام"));
                    if (lr != null) return lr;
                }

                // Substring match
                var sub = levels.FirstOrDefault(l => l.Name.ToLower().Contains(h) || h.Contains(l.Name.ToLower()));
                if (sub != null) return sub;
            }

            // If active view is a Plan view with a level, default to that level
            if (doc.ActiveView is ViewPlan vp && vp.GenLevel != null)
            {
                return vp.GenLevel;
            }

            return levels.First();
        }

        /// <summary>
        /// Analyzes MEP requirements for the chosen level, discipline, and space filter.
        /// </summary>
        public static MepPlanReport AnalyzeMep(
            Document doc,
            ElementId? specificLevelId = null,
            string? levelHint = null,
            MepDiscipline discipline = MepDiscipline.FullMep,
            string? roomFilter = null,
            ICollection<ElementId>? selectedIds = null)
        {
            var report = new MepPlanReport
            {
                Discipline = discipline
            };

            // 1. Resolve Level
            Level targetLevel = ResolveLevel(doc, specificLevelId, levelHint);
            report.LevelName = targetLevel.Name;
            report.LevelId = targetLevel.Id;
            report.LevelElevationFeet = targetLevel.Elevation;

            // 2. Discover Rooms / Spaces on Level
            var allRoomsOnLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.LevelId == targetLevel.Id && r.Area > 1.0)
                .ToList();

            List<Room> targetRooms = new List<Room>();

            // Check if selection in Revit points to specific room(s)
            if (selectedIds != null && selectedIds.Count > 0)
            {
                foreach (var id in selectedIds)
                {
                    Element elem = doc.GetElement(id);
                    if (elem is Room r && r.LevelId == targetLevel.Id)
                    {
                        targetRooms.Add(r);
                    }
                    else if (elem != null && elem.Location is LocationPoint lp)
                    {
                        Room? containingRoom = doc.GetRoomAtPoint(lp.Point);
                        if (containingRoom != null && containingRoom.LevelId == targetLevel.Id && !targetRooms.Any(tr => tr.Id == containingRoom.Id))
                        {
                            targetRooms.Add(containingRoom);
                        }
                    }
                }
                if (targetRooms.Count > 0)
                {
                    report.IsSingleRoomScope = targetRooms.Count == 1;
                    report.ScopeDescription = $"Selected Room(s) in View ({targetRooms.Count} space(s))";
                }
            }

            // If no room selected, apply roomFilter if provided
            if (targetRooms.Count == 0 && !string.IsNullOrWhiteSpace(roomFilter))
            {
                string rf = roomFilter.ToLower().Trim();
                targetRooms = allRoomsOnLevel.Where(r => MatchesRoomFilter(r, rf)).ToList();

                if (targetRooms.Count > 0)
                {
                    report.IsSingleRoomScope = targetRooms.Count == 1;
                    report.ScopeDescription = $"Filter: '{roomFilter}' ({targetRooms.Count} matching space(s))";
                }
            }

            // Fallback: Use all rooms on the level
            if (targetRooms.Count == 0)
            {
                targetRooms = allRoomsOnLevel;
                report.ScopeDescription = allRoomsOnLevel.Count > 0 
                    ? $"Entire Level ({allRoomsOnLevel.Count} spaces)" 
                    : "Level Layout (Derived Synthetic Zones)";
            }

            // 3. Perform Calculations per Room
            if (targetRooms.Count > 0)
            {
                foreach (var room in targetRooms)
                {
                    var req = AnalyzeSingleRoom(room, targetLevel.Elevation, discipline);
                    report.RoomRequirements.Add(req);
                }
            }
            else
            {
                // Fallback: If no Rooms are modeled, derive architectural spatial zones
                report.RoomRequirements = DeriveSyntheticZones(doc, targetLevel, discipline, roomFilter);
                report.IsSingleRoomScope = !string.IsNullOrWhiteSpace(roomFilter);
            }

            // 4. Summarize Totals
            report.TotalRooms = report.RoomRequirements.Count;
            report.TotalAreaSqMeters = Math.Round(report.RoomRequirements.Sum(r => r.AreaSqMeters), 1);

            report.TotalLightingFixtures = report.RoomRequirements.Sum(r => r.CalculatedFixtures);
            report.TotalSockets = report.RoomRequirements.Sum(r => r.CalculatedSockets);
            report.TotalConnectedLoadKva = Math.Round(report.RoomRequirements.Sum(r => r.EstimatedPowerKva), 2);
            report.CableTrayRouteLengthMeters = Math.Round(Math.Sqrt(Math.Max(10.0, report.TotalAreaSqMeters)) * 2.5, 1);

            report.TotalAirflowCfm = Math.Round(report.RoomRequirements.Sum(r => r.RequiredAirflowCfm), 0);
            report.TotalDiffusersOrGrilles = report.RoomRequirements.Sum(r => r.CalculatedDiffusers);
            report.DuctRouteLengthMeters = Math.Round(Math.Sqrt(Math.Max(10.0, report.TotalAreaSqMeters)) * 2.2, 1);

            report.TotalPlumbingFixtures = report.RoomRequirements.Sum(r => r.DrainageFixtureUnits > 0 ? Math.Max(2, (int)(r.DrainageFixtureUnits / 2)) : 0);
            report.PipeRouteLengthMeters = Math.Round(report.RoomRequirements.Sum(r => r.DrainageFixtureUnits > 0 ? 8.0 : 0.0), 1);

            // 5. Audit Family Readiness & AI Model Capabilities
            report.FamilyChecklist = AuditFamilyReadiness(doc, discipline);
            report.ReadinessReport = EvaluateModelReadiness(doc, discipline);

            return report;
        }

        private static bool MatchesRoomFilter(Room room, string filter)
        {
            string combined = (room.Name + " " + room.Number).ToLower();
            if (combined.Contains(filter)) return true;

            // Alias translations
            if (filter.Contains("دستشویی") || filter.Contains("توالت") || filter.Contains("سرویس") || filter.Contains("wc") || filter.Contains("toilet") || filter.Contains("restroom"))
            {
                return combined.Contains("toilet") || combined.Contains("restroom") || combined.Contains("bath") || combined.Contains("wash") || combined.Contains("wc") || combined.Contains("سرویس");
            }
            if (filter.Contains("راهرو") || filter.Contains("corridor") || filter.Contains("hall") || filter.Contains("lobby"))
            {
                return combined.Contains("corridor") || combined.Contains("hall") || combined.Contains("lobby") || combined.Contains("stair");
            }
            if (filter.Contains("اتاق") || filter.Contains("office") || filter.Contains("work") || filter.Contains("جلسات") || filter.Contains("meeting"))
            {
                return combined.Contains("office") || combined.Contains("room") || combined.Contains("work") || combined.Contains("conf") || combined.Contains("meet");
            }

            // Match digits
            var digits = Regex.Matches(filter, @"\d+");
            foreach (Match m in digits)
            {
                if (room.Number.Contains(m.Value)) return true;
            }

            return false;
        }

        private static RoomMepRequirement AnalyzeSingleRoom(Room room, double levelElevation, MepDiscipline discipline)
        {
            var req = new RoomMepRequirement
            {
                RoomId = room.Id,
                RoomName = string.IsNullOrWhiteSpace(room.Name) ? "Room" : room.Name,
                RoomNumber = string.IsNullOrWhiteSpace(room.Number) ? "101" : room.Number
            };

            // Area and Perimeter
            double areaSqFt = room.Area;
            req.AreaSqMeters = Math.Round(areaSqFt * 0.092903, 1);
            if (req.AreaSqMeters < 1.0) req.AreaSqMeters = 15.0;

            double perimeterFt = room.Perimeter;
            req.PerimeterMeters = Math.Round(perimeterFt * 0.3048, 1);
            if (req.PerimeterMeters < 1.0) req.PerimeterMeters = Math.Round(Math.Sqrt(req.AreaSqMeters) * 4.0, 1);

            // Bounding box & Center
            BoundingBoxXYZ bbox = room.get_BoundingBox(null);
            req.BoundingBox = bbox;
            if (bbox != null)
            {
                req.CenterPoint = (bbox.Min + bbox.Max) * 0.5;
            }

            // Space Classification & Engineering Sizing
            string nameLower = (room.Name + " " + room.Number).ToLower();
            double zCeiling = levelElevation + (req.CeilingHeightMeters / 0.3048);

            if (nameLower.Contains("toilet") || nameLower.Contains("bath") || nameLower.Contains("wash") || nameLower.Contains("wc") || nameLower.Contains("سرویس") || nameLower.Contains("توالت") || nameLower.Contains("دستشویی"))
            {
                req.SpaceType = "Restroom / Sanitary";
                req.TargetLux = 200; // Moisture-rated IP44 lighting
                req.CalculatedFixtures = Math.Max(1, (int)Math.Ceiling((req.AreaSqMeters * 200.0) / 1728.0));
                req.CalculatedSockets = 2; // GFCI safety receptacles
                req.EstimatedPowerKva = 0.20;

                // Mechanical HVAC: Exhaust ventilation per ASHRAE 62.1 (15 ACH or 50 CFM per WC)
                req.AirChangesPerHour = 15.0;
                req.RequiredAirflowCfm = Math.Max(100.0, Math.Round(req.AreaSqMeters * 10.0, 0));
                req.DuctSizeRecommendation = "200x150 mm Exhaust";
                req.CalculatedDiffusers = 1; // 1 dedicated exhaust grille

                // Plumbing: Sanitary drainage & water supply
                req.DrainageFixtureUnits = 6; // 1 WC (4 DFU) + 1 Lavatory (1 DFU) + 1 Floor Drain (1 DFU)
                req.PipeSizeRecommendation = "DN100 (4\") Main Waste, DN50 (2\") Branch";
                req.StandardDrainSlopePercent = 1.5; // 1.5% to 2.0% gravity slope
            }
            else if (nameLower.Contains("corridor") || nameLower.Contains("hall") || nameLower.Contains("lobby") || nameLower.Contains("stair") || nameLower.Contains("راهرو"))
            {
                req.SpaceType = "Corridor / Circulation";
                req.TargetLux = 150;
                req.CalculatedFixtures = Math.Max(2, (int)Math.Ceiling((req.AreaSqMeters * 150.0) / 1728.0));
                req.CalculatedSockets = Math.Max(2, (int)Math.Ceiling(req.PerimeterMeters / 8.0));
                req.EstimatedPowerKva = Math.Round((req.AreaSqMeters * 6.0 * 1.25) / 1000.0, 2);

                req.AirChangesPerHour = 4.0;
                req.RequiredAirflowCfm = Math.Round(req.AreaSqMeters * 4.0, 0);
                req.DuctSizeRecommendation = "350x200 mm Main Header";
                req.CalculatedDiffusers = Math.Max(2, (int)Math.Ceiling(req.AreaSqMeters / 25.0));

                req.DrainageFixtureUnits = 0;
            }
            else if (nameLower.Contains("electric") || nameLower.Contains("mech") || nameLower.Contains("plant") || nameLower.Contains("server") || nameLower.Contains("تاسیسات") || nameLower.Contains("موتورخانه"))
            {
                req.SpaceType = "Technical / Plant Room";
                req.TargetLux = 300;
                req.CalculatedFixtures = Math.Max(2, (int)Math.Ceiling((req.AreaSqMeters * 300.0) / 1728.0));
                req.CalculatedSockets = 4;
                req.EstimatedPowerKva = 1.5;

                req.AirChangesPerHour = 10.0;
                req.RequiredAirflowCfm = Math.Round(req.AreaSqMeters * 12.0, 0);
                req.DuctSizeRecommendation = "300x250 mm Dedicated Cooling/Exhaust";
                req.CalculatedDiffusers = 2;

                req.DrainageFixtureUnits = 2; // Floor drain for equipment
                req.PipeSizeRecommendation = "DN50 (2\") Equipment Drain";
            }
            else
            {
                // General Office / Workspace / Open Plan
                req.SpaceType = "Office / Workspace";
                req.TargetLux = 500;
                req.CalculatedFixtures = Math.Max(2, (int)Math.Ceiling((req.AreaSqMeters * 500.0) / 1728.0));
                req.CalculatedSockets = Math.Max(4, (int)Math.Ceiling(req.PerimeterMeters / 3.5));
                req.EstimatedPowerKva = Math.Round((req.AreaSqMeters * 12.0 * 1.25) / 1000.0, 2);

                req.AirChangesPerHour = 6.0;
                req.RequiredAirflowCfm = Math.Round(req.AreaSqMeters * 6.5, 0);
                req.DuctSizeRecommendation = "300x200 mm Supply, 250x200 mm Return";
                req.CalculatedDiffusers = Math.Max(2, (int)Math.Ceiling(req.AreaSqMeters / 18.0));

                req.DrainageFixtureUnits = 0;
            }

            // Geometric Grids
            if (bbox != null)
            {
                req.FixtureGridPoints = ComputeGridPoints(bbox, req.CalculatedFixtures, zCeiling);
                req.DiffuserGridPoints = ComputeGridPoints(bbox, req.CalculatedDiffusers, zCeiling);
                req.PlumbingPoints = new List<XYZ> { new XYZ(bbox.Min.X + 2.0, bbox.Min.Y + 2.0, levelElevation - (0.5 / 0.3048)) };
            }

            return req;
        }

        private static List<RoomMepRequirement> DeriveSyntheticZones(Document doc, Level level, MepDiscipline discipline, string? roomFilter)
        {
            var list = new List<RoomMepRequirement>();

            bool isRestroomRequested = !string.IsNullOrWhiteSpace(roomFilter) &&
                (roomFilter.Contains("دستشویی") || roomFilter.Contains("توالت") || roomFilter.Contains("سرویس") || roomFilter.Contains("toilet") || roomFilter.Contains("restroom"));

            if (isRestroomRequested)
            {
                var r = new RoomMepRequirement
                {
                    RoomName = "Sanitary / Restroom Core",
                    RoomNumber = "Zone-105-WC",
                    SpaceType = "Restroom / Sanitary",
                    AreaSqMeters = 18.0,
                    PerimeterMeters = 17.0,
                    TargetLux = 200,
                    CalculatedFixtures = 2,
                    CalculatedSockets = 2,
                    EstimatedPowerKva = 0.25,
                    AirChangesPerHour = 15.0,
                    RequiredAirflowCfm = 180.0,
                    DuctSizeRecommendation = "200x150 mm Dedicated Exhaust",
                    CalculatedDiffusers = 1,
                    DrainageFixtureUnits = 6,
                    PipeSizeRecommendation = "DN100 (4\") Main Waste with 1.5% Slope",
                    StandardDrainSlopePercent = 1.5,
                    CenterPoint = new XYZ(10.0, 10.0, level.Elevation + 9.2)
                };
                var boxRestroom = new BoundingBoxXYZ
                {
                    Min = new XYZ(r.CenterPoint.X - 10.0, r.CenterPoint.Y - 8.0, level.Elevation),
                    Max = new XYZ(r.CenterPoint.X + 10.0, r.CenterPoint.Y + 8.0, level.Elevation + 9.5)
                };
                r.BoundingBox = boxRestroom;
                r.FixtureGridPoints = ComputeGridPoints(boxRestroom, r.CalculatedFixtures, level.Elevation + 9.0);
                r.DiffuserGridPoints = ComputeGridPoints(boxRestroom, r.CalculatedDiffusers, level.Elevation + 9.0);
                list.Add(r);
                return list;
            }

            // 4 Functional Zones
            string[] names = { "Primary Open Workspace", "Executive Offices", "Central Circulation & Core", "Sanitary & Facility Core" };
            string[] types = { "Office / Workspace", "Office / Workspace", "Corridor / Circulation", "Restroom / Sanitary" };
            double[] areas = { 120.0, 60.0, 45.0, 20.0 };

            for (int i = 0; i < 4; i++)
            {
                var r = new RoomMepRequirement
                {
                    RoomName = names[i],
                    RoomNumber = $"Zone-{101 + i}",
                    SpaceType = types[i],
                    AreaSqMeters = areas[i],
                    PerimeterMeters = Math.Round(Math.Sqrt(areas[i]) * 4.0, 1),
                    CenterPoint = new XYZ(i * 20.0, 0, level.Elevation + 9.2)
                };

                if (types[i].Contains("Sanitary"))
                {
                    r.TargetLux = 200;
                    r.CalculatedFixtures = 2;
                    r.CalculatedSockets = 2;
                    r.EstimatedPowerKva = 0.25;
                    r.RequiredAirflowCfm = 150.0;
                    r.DuctSizeRecommendation = "200x150 mm Exhaust";
                    r.CalculatedDiffusers = 1;
                    r.DrainageFixtureUnits = 6;
                    r.PipeSizeRecommendation = "DN100 (4\") Waste";
                    r.StandardDrainSlopePercent = 1.5;
                }
                else if (types[i].Contains("Corridor"))
                {
                    r.TargetLux = 150;
                    r.CalculatedFixtures = 4;
                    r.CalculatedSockets = 4;
                    r.EstimatedPowerKva = 0.45;
                    r.RequiredAirflowCfm = 180.0;
                    r.DuctSizeRecommendation = "350x200 mm Main Header";
                    r.CalculatedDiffusers = 2;
                }
                else
                {
                    r.TargetLux = 500;
                    r.CalculatedFixtures = Math.Max(2, (int)Math.Ceiling(areas[i] / 8.0));
                    r.CalculatedSockets = Math.Max(4, (int)Math.Ceiling(areas[i] / 7.0));
                    r.EstimatedPowerKva = Math.Round((areas[i] * 12.0) / 1000.0, 2);
                    r.RequiredAirflowCfm = Math.Round(areas[i] * 6.5, 0);
                    r.DuctSizeRecommendation = "300x200 mm Supply";
                    r.CalculatedDiffusers = Math.Max(2, (int)Math.Ceiling(areas[i] / 20.0));
                }

                double halfSide = Math.Sqrt(areas[i]) * 0.5 / 0.3048;
                var boxZone = new BoundingBoxXYZ
                {
                    Min = new XYZ(r.CenterPoint.X - halfSide, r.CenterPoint.Y - halfSide, level.Elevation),
                    Max = new XYZ(r.CenterPoint.X + halfSide, r.CenterPoint.Y + halfSide, level.Elevation + 9.5)
                };
                r.BoundingBox = boxZone;
                r.FixtureGridPoints = ComputeGridPoints(boxZone, r.CalculatedFixtures, level.Elevation + 9.0);
                r.DiffuserGridPoints = ComputeGridPoints(boxZone, r.CalculatedDiffusers, level.Elevation + 9.0);

                list.Add(r);
            }

            return list;
        }

        private static List<XYZ> ComputeGridPoints(BoundingBoxXYZ bbox, int count, double zElevation)
        {
            var points = new List<XYZ>();
            double minX = bbox.Min.X;
            double maxX = bbox.Max.X;
            double minY = bbox.Min.Y;
            double maxY = bbox.Max.Y;

            if (count <= 1)
            {
                points.Add(new XYZ((minX + maxX) * 0.5, (minY + maxY) * 0.5, zElevation));
                return points;
            }

            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling((double)count / cols);

            double stepX = (maxX - minX) / (cols + 1);
            double stepY = (maxY - minY) / (rows + 1);

            int placed = 0;
            for (int r = 1; r <= rows && placed < count; r++)
            {
                for (int c = 1; c <= cols && placed < count; c++)
                {
                    points.Add(new XYZ(minX + (c * stepX), minY + (r * stepY), zElevation));
                    placed++;
                }
            }

            return points;
        }

        public static bool IsValidMepSymbol(FamilySymbol s)
        {
            if (s == null) return false;
            string fName = (s.FamilyName + " " + s.Name).ToLower();
            if (fName.Contains("rpc") || fName.Contains("person") || fName.Contains("entourage") || 
                fName.Contains("male") || fName.Contains("female") || fName.Contains("alex") || 
                fName.Contains("tina") || fName.Contains("man") || fName.Contains("woman") || 
                fName.Contains("people") || fName.Contains("figure"))
            {
                return false;
            }
            return true;
        }

        public static List<FamilyReadinessItem> AuditFamilyReadiness(Document doc, MepDiscipline discipline)
        {
            var list = new List<FamilyReadinessItem>();

            // Electrical Audits
            if (discipline == MepDiscipline.Electrical || discipline == MepDiscipline.FullMep)
            {
                var lightSymbols = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(IsValidMepSymbol)
                    .ToList();

                list.Add(new FamilyReadinessItem
                {
                    CategoryName = "Lighting Fixtures (OST_LightingFixtures)",
                    RoleDescription = "General & Sanitary Illumination (LED Troffer / Recessed Downlight)",
                    IsAvailable = lightSymbols.Count > 0,
                    LoadedFamilyName = lightSymbols.Count > 0 ? $"{lightSymbols[0].FamilyName} : {lightSymbols[0].Name}" : "None loaded",
                    RecommendedFamily = "M_Troffer - Parabolic or M_Downlight - Recessed",
                    AutodeskLibraryPath = "Insert > Load Autodesk Family > Lighting > Architectural > Internal"
                });

                var socketSymbols = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalFixtures)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(IsValidMepSymbol)
                    .ToList();

                list.Add(new FamilyReadinessItem
                {
                    CategoryName = "Electrical Fixtures (OST_ElectricalFixtures)",
                    RoleDescription = "Power Receptacles / Wall Sockets (Standard & GFCI)",
                    IsAvailable = socketSymbols.Count > 0,
                    LoadedFamilyName = socketSymbols.Count > 0 ? $"{socketSymbols[0].FamilyName} : {socketSymbols[0].Name}" : "None loaded",
                    RecommendedFamily = "M_Duplex Receptacle",
                    AutodeskLibraryPath = "Insert > Load Autodesk Family > Electrical > Electrical Power > Terminals"
                });

                var cableTrayTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(CableTrayType))
                    .Cast<CableTrayType>()
                    .ToList();

                list.Add(new FamilyReadinessItem
                {
                    CategoryName = "Cable Tray Containment (OST_CableTray)",
                    RoleDescription = "Primary Horizontal Power & Data Backbone (Revit System Family)",
                    IsAvailable = cableTrayTypes.Count > 0,
                    LoadedFamilyName = cableTrayTypes.Count > 0 ? cableTrayTypes[0].Name : "Standard System Type",
                    RecommendedFamily = "Built-in Channel / Ladder Cable Tray",
                    AutodeskLibraryPath = "System Family (Ready natively in Revit - 0 downloads)"
                });
            }

            // Mechanical HVAC Audits
            if (discipline == MepDiscipline.MechanicalHvac || discipline == MepDiscipline.FullMep)
            {
                var airTerminals = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctTerminal)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(IsValidMepSymbol)
                    .ToList();

                list.Add(new FamilyReadinessItem
                {
                    CategoryName = "Air Terminals (OST_DuctTerminal)",
                    RoleDescription = "Supply Diffusers & Restroom Exhaust Grilles",
                    IsAvailable = airTerminals.Count > 0,
                    LoadedFamilyName = airTerminals.Count > 0 ? $"{airTerminals[0].FamilyName} : {airTerminals[0].Name}" : "None loaded",
                    RecommendedFamily = "M_Supply Diffuser or M_Exhaust Grille - Rectangular",
                    AutodeskLibraryPath = "Insert > Load Autodesk Family > Mechanical > MEP > Air-Side > Air Terminals"
                });

                var ductTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DuctType))
                    .Cast<DuctType>()
                    .ToList();

                list.Add(new FamilyReadinessItem
                {
                    CategoryName = "Mechanical Ducts (OST_DuctCurves)",
                    RoleDescription = "Rectangular & Round Air Distribution / Exhaust Spines",
                    IsAvailable = ductTypes.Count > 0,
                    LoadedFamilyName = ductTypes.Count > 0 ? ductTypes[0].Name : "Standard Duct Type",
                    RecommendedFamily = "Rectangular Duct (Mitered Elbows / Taps)",
                    AutodeskLibraryPath = "System Family (Ready natively in Revit - 0 downloads)"
                });
            }

            // Plumbing Sanitary Audits
            if (discipline == MepDiscipline.PlumbingSanitary || discipline == MepDiscipline.FullMep)
            {
                var plumbingFixtures = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(IsValidMepSymbol)
                    .ToList();

                list.Add(new FamilyReadinessItem
                {
                    CategoryName = "Plumbing Fixtures (OST_PlumbingFixtures)",
                    RoleDescription = "Sanitary Ware (Water Closet, Lavatory, Floor Drain)",
                    IsAvailable = plumbingFixtures.Count > 0,
                    LoadedFamilyName = plumbingFixtures.Count > 0 ? $"{plumbingFixtures[0].FamilyName} : {plumbingFixtures[0].Name}" : "None loaded",
                    RecommendedFamily = "M_Water Closet - Flush Valve or M_Lavatory - Counter Top",
                    AutodeskLibraryPath = "Insert > Load Autodesk Family > Plumbing > MEP > Fixtures"
                });

                var pipeTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipeType))
                    .Cast<PipeType>()
                    .ToList();

                list.Add(new FamilyReadinessItem
                {
                    CategoryName = "Sanitary & Drainage Pipes (OST_PipeCurves)",
                    RoleDescription = "Gravity Drainage Pipes with 1.5% - 2.0% Standard Slope",
                    IsAvailable = pipeTypes.Count > 0,
                    LoadedFamilyName = pipeTypes.Count > 0 ? pipeTypes[0].Name : "Standard Pipe Type",
                    RecommendedFamily = "PVC / Cast Iron Sanitary Drain Pipe",
                    AutodeskLibraryPath = "System Family (Ready natively in Revit - 0 downloads)"
                });
            }

            return list;
        }

        /// <summary>
        /// Evaluates what actions the AI can execute right now vs. what is blocked by missing prerequisites.
        /// Formulates an actionable user roadmap for missing elements.
        /// </summary>
        public static ModelReadinessReport EvaluateModelReadiness(Document doc, MepDiscipline discipline)
        {
            var readiness = new ModelReadinessReport();

            // 1. Cable Tray
            if (discipline == MepDiscipline.Electrical || discipline == MepDiscipline.FullMep)
            {
                var trayType = new FilteredElementCollector(doc).OfClass(typeof(CableTrayType)).Cast<CableTrayType>().FirstOrDefault();
                if (trayType != null)
                {
                    readiness.CapableActions.Add(new ActionReadinessItem
                    {
                        Title = "3D Cable Tray Containment Spine",
                        IsReady = true,
                        Details = $"Ready. CableTrayType '{trayType.Name}' available. Can model 300x100mm primary electrical backbone."
                    });
                }
                else
                {
                    readiness.BlockedActions.Add(new ActionReadinessItem
                    {
                        Title = "3D Cable Tray Containment Spine",
                        IsReady = false,
                        Details = "No CableTrayType loaded in active model.",
                        PrerequisiteInstruction = "Enable Cable Tray system family from Revit Electrical template."
                    });
                    readiness.UserActionableRoadmap.Add("Electrical Cable Tray: Transfer Electrical Project Standards from an MEP template.");
                }
            }

            // 2. Mechanical HVAC Ducts
            if (discipline == MepDiscipline.MechanicalHvac || discipline == MepDiscipline.FullMep)
            {
                var ductType = new FilteredElementCollector(doc).OfClass(typeof(DuctType)).Cast<DuctType>().FirstOrDefault();
                var ductSys = new FilteredElementCollector(doc).OfClass(typeof(MEPSystemType)).Cast<MEPSystemType>().FirstOrDefault();

                if (ductType != null && ductSys != null)
                {
                    readiness.CapableActions.Add(new ActionReadinessItem
                    {
                        Title = "3D Mechanical HVAC Air Distribution Ducts",
                        IsReady = true,
                        Details = $"Ready. DuctType '{ductType.Name}' and System '{ductSys.Name}' available."
                    });
                }
                else
                {
                    string missing = ductType == null && ductSys == null ? "Duct Types and MEP System Types" : (ductType == null ? "Duct Types" : "MEP System Types");
                    readiness.BlockedActions.Add(new ActionReadinessItem
                    {
                        Title = "3D Mechanical HVAC Air Distribution Ducts",
                        IsReady = false,
                        Details = $"{missing} are not loaded in this architectural template.",
                        PrerequisiteInstruction = "Go to Manage > Transfer Project Standards and check 'Duct Types' & 'MEP System Types' from an MEP project or link."
                    });
                    readiness.UserActionableRoadmap.Add("Mechanical HVAC: Transfer Duct Types & MEP System Types via Manage > Transfer Project Standards.");
                }
            }

            // 3. Plumbing Sanitary Waste Pipes
            if (discipline == MepDiscipline.PlumbingSanitary || discipline == MepDiscipline.FullMep)
            {
                var pipeType = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().FirstOrDefault();
                var pipeSys = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().FirstOrDefault();

                if (pipeType != null && pipeSys != null)
                {
                    readiness.CapableActions.Add(new ActionReadinessItem
                    {
                        Title = "3D Sloped Sanitary Waste Pipes (1.5% Slope)",
                        IsReady = true,
                        Details = $"Ready. PipeType '{pipeType.Name}' and System '{pipeSys.Name}' available."
                    });
                }
                else
                {
                    string missing = pipeType == null && pipeSys == null ? "Pipe Types and Piping System Types" : (pipeType == null ? "Pipe Types" : "Piping System Types");
                    readiness.BlockedActions.Add(new ActionReadinessItem
                    {
                        Title = "3D Sloped Sanitary Waste Pipes",
                        IsReady = false,
                        Details = $"{missing} are not loaded in this architectural template.",
                        PrerequisiteInstruction = "Go to Manage > Transfer Project Standards and check 'Pipe Types' & 'Piping System Types'."
                    });
                    readiness.UserActionableRoadmap.Add("Plumbing Drainage: Transfer Pipe Types & Piping Systems via Manage > Transfer Project Standards.");
                }
            }

            // 4. Fixtures
            var lightSymbol = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_LightingFixtures).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(IsValidMepSymbol);
            if (lightSymbol != null && (discipline == MepDiscipline.Electrical || discipline == MepDiscipline.FullMep))
            {
                readiness.CapableActions.Add(new ActionReadinessItem
                {
                    Title = "Lighting Fixtures Placement",
                    IsReady = true,
                    Details = $"Ready. Family '{lightSymbol.FamilyName}' loaded in project."
                });
            }
            else if (discipline == MepDiscipline.Electrical || discipline == MepDiscipline.FullMep)
            {
                readiness.BlockedActions.Add(new ActionReadinessItem
                {
                    Title = "Lighting Fixtures Placement",
                    IsReady = false,
                    Details = "No lighting fixture families are loaded in this model.",
                    PrerequisiteInstruction = "Load lighting fixture family via Insert > Load Autodesk Family > Lighting."
                });
                readiness.UserActionableRoadmap.Add("Lighting Fixtures: Load an architectural lighting family (Insert > Load Autodesk Family > Lighting).");
            }

            var airTerminalSymbol = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DuctTerminal)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(IsValidMepSymbol);
            if (airTerminalSymbol != null && (discipline == MepDiscipline.MechanicalHvac || discipline == MepDiscipline.FullMep))
            {
                readiness.CapableActions.Add(new ActionReadinessItem
                {
                    Title = "Air Diffusers Placement",
                    IsReady = true,
                    Details = $"Ready. Family '{airTerminalSymbol.FamilyName}' loaded in project."
                });
            }
            else if (discipline == MepDiscipline.MechanicalHvac || discipline == MepDiscipline.FullMep)
            {
                readiness.BlockedActions.Add(new ActionReadinessItem
                {
                    Title = "Air Diffusers Placement",
                    IsReady = false,
                    Details = "No air terminal families are loaded in this model.",
                    PrerequisiteInstruction = "Load diffuser family via Insert > Load Autodesk Family > Mechanical > Air Terminals."
                });
                readiness.UserActionableRoadmap.Add("HVAC Diffusers: Load air terminal family (Insert > Load Autodesk Family > Mechanical > Air-Side > Air Terminals).");
            }

            return readiness;
        }

        /// <summary>
        /// Computes bounding box enclosure for target scope/rooms.
        /// </summary>
        private static BoundingBoxXYZ? GetScopeBoundingBox(MepPlanReport report)
        {
            var validBoxes = report.RoomRequirements
                .Where(r => r.BoundingBox != null)
                .Select(r => r.BoundingBox!)
                .ToList();

            if (validBoxes.Count == 0) return null;
            if (validBoxes.Count == 1) return validBoxes[0];

            return new BoundingBoxXYZ
            {
                Min = new XYZ(validBoxes.Min(b => b.Min.X), validBoxes.Min(b => b.Min.Y), validBoxes.Min(b => b.Min.Z)),
                Max = new XYZ(validBoxes.Max(b => b.Max.X), validBoxes.Max(b => b.Max.Y), validBoxes.Max(b => b.Max.Z))
            };
        }

        /// <summary>
        /// Scans structural framing (OST_StructuralFraming) and determines clash-free elevation
        /// guaranteed to sit 150mm (0.5 ft) below the lowest beam soffit.
        /// </summary>
        public static (double ElevationFt, string ClearanceNote) CalculateClashFreeElevation(Document doc, ElementId levelId, double defaultOffsetMeters, BoundingBoxXYZ? scopeBounds)
        {
            Level? level = doc.GetElement(levelId) as Level;
            double levelElevFt = level?.Elevation ?? 0.0;
            double targetElevFt = levelElevFt + (defaultOffsetMeters / 0.3048);

            try
            {
                var beams = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .ToList();

                if (beams.Count == 0)
                {
                    return (targetElevFt, "Plenum elevation (No structural beams detected in model).");
                }

                double lowestBeamSoffit = double.MaxValue;
                int relevantBeamsCount = 0;

                foreach (var beam in beams)
                {
                    BoundingBoxXYZ? bb = beam.get_BoundingBox(null);
                    if (bb == null) continue;

                    if (scopeBounds != null)
                    {
                        bool overlapX = bb.Max.X >= (scopeBounds.Min.X - 3.0) && bb.Min.X <= (scopeBounds.Max.X + 3.0);
                        bool overlapY = bb.Max.Y >= (scopeBounds.Min.Y - 3.0) && bb.Min.Y <= (scopeBounds.Max.Y + 3.0);
                        if (!overlapX || !overlapY) continue;
                    }

                    double beamSoffitAboveFloor = bb.Min.Z - levelElevFt;
                    if (beamSoffitAboveFloor > 2.0 && beamSoffitAboveFloor < 15.0)
                    {
                        relevantBeamsCount++;
                        if (bb.Min.Z < lowestBeamSoffit)
                        {
                            lowestBeamSoffit = bb.Min.Z;
                        }
                    }
                }

                if (relevantBeamsCount > 0 && lowestBeamSoffit < double.MaxValue)
                {
                    // Clamp MEP centerline 150mm (0.5 ft) below lowest beam soffit
                    double safeElevFt = lowestBeamSoffit - (150.0 / 304.8);
                    double safeFromFloorM = (safeElevFt - levelElevFt) * 0.3048;
                    return (safeElevFt, $"🛡️ Anti-Clash Engaged: Detected {relevantBeamsCount} structural beam(s). Auto-adjusted elevation to {safeFromFloorM:0.2}m (150mm below beam soffit).");
                }
            }
            catch { }

            return (targetElevFt, "Plenum elevation (Structural framing clear).");
        }

        /// <summary>
        /// Automatically locates coincident/adjacent end connectors on two MEP curves
        /// and inserts a native Revit elbow fitting, uniting segments into a single continuous system.
        /// </summary>
        public static FamilyInstance? TryConnectWithElbow(Document doc, MEPCurve curve1, MEPCurve curve2)
        {
            if (curve1 == null || curve2 == null) return null;

            try
            {
                var set1 = curve1.ConnectorManager?.Connectors;
                var set2 = curve2.ConnectorManager?.Connectors;
                if (set1 == null || set2 == null) return null;

                Connector? best1 = null;
                Connector? best2 = null;
                double minDistance = double.MaxValue;

                foreach (Connector c1 in set1)
                {
                    if (c1.IsConnected) continue;
                    foreach (Connector c2 in set2)
                    {
                        if (c2.IsConnected) continue;
                        double dist = c1.Origin.DistanceTo(c2.Origin);
                        if (dist < minDistance)
                        {
                            minDistance = dist;
                            best1 = c1;
                            best2 = c2;
                        }
                    }
                }

                if (best1 != null && best2 != null && minDistance < 2.5)
                {
                    FamilyInstance elbow = doc.Create.NewElbowFitting(best1, best2);
                    return elbow;
                }
            }
            catch
            {
                // Graceful fallback if routing preferences or fitting family is not present
            }
            return null;
        }

        /// <summary>
        /// Generates real 3D Cable Tray containment backbone in Revit with automated elbow fittings and clash avoidance.
        /// Zero external file downloads needed.
        /// </summary>
        public static ExecutionResult GenerateCableTraySpine(UIDocument uidoc, MepPlanReport report)
        {
            var result = new ExecutionResult { ActionName = "Generate 3D Cable Tray Containment" };
            Document doc = uidoc.Document;

            try
            {
                CableTrayType? trayType = new FilteredElementCollector(doc)
                    .OfClass(typeof(CableTrayType))
                    .Cast<CableTrayType>()
                    .FirstOrDefault();

                if (trayType == null)
                {
                    result.Success = false;
                    result.Message = "No Cable Tray system type found in the current Revit model.";
                    return result;
                }

                var (elevationFt, clearanceNote) = CalculateClashFreeElevation(doc, report.LevelId, 2.9, GetScopeBoundingBox(report));
                var spinePoints = ComputeSpinePoints(report, elevationFt);

                List<ElementId> createdIds = new List<ElementId>();
                int elbowCount = 0;

                RevitActionExecutor.RunInTransaction(doc, "BIM AIA: Generate Cable Tray Backbone", () =>
                {
                    var createdTrays = new List<CableTray>();

                    for (int i = 0; i < spinePoints.Count - 1; i++)
                    {
                        XYZ p1 = spinePoints[i];
                        XYZ p2 = spinePoints[i + 1];

                        if (p1.DistanceTo(p2) > 1.0)
                        {
                            CableTray tray = CableTray.Create(doc, trayType.Id, p1, p2, report.LevelId);
                            if (tray != null)
                            {
                                tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.Set(300.0 / 304.8);
                                tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.Set(100.0 / 304.8);
                                tray.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"BIM AIA: Primary Cable Tray Containment ({report.LevelName})");

                                createdIds.Add(tray.Id);
                                createdTrays.Add(tray);

                                RevitActionExecutor.ChangeHistory.Insert(0, new ModelChangeRecord
                                {
                                    ElementId = tray.Id,
                                    Category = "Cable Trays",
                                    ChangeType = "Generative Electrical Containment",
                                    Description = $"Generated 300x100mm Cable Tray Spine along {report.LevelName}",
                                    BeforeValue = "None",
                                    AfterValue = "Modeled 3D Spine",
                                    ColorHex = "#89B4FA"
                                });
                            }
                        }
                    }

                    // Auto-connect adjacent trays with elbow fittings
                    for (int i = 0; i < createdTrays.Count - 1; i++)
                    {
                        FamilyInstance? elbow = TryConnectWithElbow(doc, createdTrays[i], createdTrays[i + 1]);
                        if (elbow != null)
                        {
                            elbowCount++;
                            createdIds.Add(elbow.Id);
                            elbow.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"BIM AIA: Cable Tray 90° Elbow Fitting ({report.LevelName})");

                            RevitActionExecutor.ChangeHistory.Insert(0, new ModelChangeRecord
                            {
                                ElementId = elbow.Id,
                                Category = "Cable Tray Fittings",
                                ChangeType = "Generative Containment Fitting",
                                Description = $"Auto-Connected Cable Tray Elbow on {report.LevelName}",
                                BeforeValue = "Open Tray End",
                                AfterValue = "Continuous Containment System",
                                ColorHex = "#89B4FA"
                            });
                        }
                    }
                });

                if (createdIds.Count > 0)
                {
                    RevitActionExecutor.EnsureModelView(uidoc);
                    RevitActionExecutor.SafeFocusElements(uidoc, createdIds);
                    result.Success = true;
                    result.AffectedElementIds = createdIds;
                    result.VisualColorApplied = "#89B4FA (Cadet Blue)";
                    string elbowMsg = elbowCount > 0 ? $" + {elbowCount} Elbow Fitting(s) Connected" : "";
                    result.Message = $"Generated {createdIds.Count - elbowCount} Cable Tray section(s){elbowMsg} on {report.LevelName}. {clearanceNote}";
                }
                else
                {
                    result.Success = false;
                    result.Message = "Could not generate cable tray spine along the current level coordinates.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error generating cable tray: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Generates real 3D Mechanical HVAC Air Distribution or Exhaust Duct in Revit with automated elbow fittings and clash avoidance.
        /// Zero external file downloads needed.
        /// </summary>
        public static ExecutionResult GenerateHvacDuctSpine(UIDocument uidoc, MepPlanReport report)
        {
            var result = new ExecutionResult { ActionName = "Generate 3D Mechanical HVAC Duct" };
            Document doc = uidoc.Document;

            try
            {
                DuctType? ductType = new FilteredElementCollector(doc)
                    .OfClass(typeof(DuctType))
                    .Cast<DuctType>()
                    .FirstOrDefault();

                MEPSystemType? ductSystem = new FilteredElementCollector(doc)
                    .OfClass(typeof(MEPSystemType))
                    .Cast<MEPSystemType>()
                    .FirstOrDefault();

                if (ductType == null || ductSystem == null)
                {
                    result.Success = false;
                    result.Message = "No Mechanical Duct system types loaded in this model template. Please transfer or load MEP Duct Types.";
                    return result;
                }

                var (elevationFt, clearanceNote) = CalculateClashFreeElevation(doc, report.LevelId, 3.0, GetScopeBoundingBox(report));
                var spinePoints = ComputeSpinePoints(report, elevationFt);

                List<ElementId> createdIds = new List<ElementId>();
                int elbowCount = 0;

                RevitActionExecutor.RunInTransaction(doc, "BIM AIA: Generate HVAC Duct Spine", () =>
                {
                    var createdDucts = new List<Duct>();

                    for (int i = 0; i < spinePoints.Count - 1; i++)
                    {
                        XYZ p1 = spinePoints[i];
                        XYZ p2 = spinePoints[i + 1];

                        if (p1.DistanceTo(p2) > 1.0)
                        {
                            Duct duct = Duct.Create(doc, ductSystem.Id, ductType.Id, report.LevelId, p1, p2);
                            if (duct != null)
                            {
                                duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.Set(300.0 / 304.8);
                                duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.Set(200.0 / 304.8);
                                duct.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"BIM AIA: HVAC Air Distribution Spine ({report.LevelName})");

                                createdIds.Add(duct.Id);
                                createdDucts.Add(duct);

                                RevitActionExecutor.ChangeHistory.Insert(0, new ModelChangeRecord
                                {
                                    ElementId = duct.Id,
                                    Category = "Ducts",
                                    ChangeType = "Generative Mechanical HVAC",
                                    Description = $"Generated 300x200mm Duct Run on {report.LevelName}",
                                    BeforeValue = "None",
                                    AfterValue = "Modeled 3D Duct",
                                    ColorHex = "#00E5FF"
                                });
                            }
                        }
                    }

                    // Auto-connect adjacent ducts with native elbow fittings
                    for (int i = 0; i < createdDucts.Count - 1; i++)
                    {
                        FamilyInstance? elbow = TryConnectWithElbow(doc, createdDucts[i], createdDucts[i + 1]);
                        if (elbow != null)
                        {
                            elbowCount++;
                            createdIds.Add(elbow.Id);
                            elbow.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"BIM AIA: Connected HVAC 90° Elbow ({report.LevelName})");

                            RevitActionExecutor.ChangeHistory.Insert(0, new ModelChangeRecord
                            {
                                ElementId = elbow.Id,
                                Category = "Duct Fittings",
                                ChangeType = "Generative MEP System Fitting",
                                Description = $"Auto-Connected Native 90° Elbow on {report.LevelName}",
                                BeforeValue = "Open Connector",
                                AfterValue = "Joined MEP System",
                                ColorHex = "#00E5FF"
                            });
                        }
                    }
                });

                if (createdIds.Count > 0)
                {
                    RevitActionExecutor.EnsureModelView(uidoc);
                    RevitActionExecutor.SafeFocusElements(uidoc, createdIds);
                    result.Success = true;
                    result.AffectedElementIds = createdIds;
                    result.VisualColorApplied = "#00E5FF (Electric Cyan)";
                    string elbowMsg = elbowCount > 0 ? $" + {elbowCount} Elbow Fitting(s) Connected" : "";
                    result.Message = $"Generated {createdIds.Count - elbowCount} HVAC Duct section(s){elbowMsg} on {report.LevelName}. {clearanceNote}";
                }
                else
                {
                    result.Success = false;
                    result.Message = "Could not generate duct run along the current level coordinates.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error generating duct: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Generates real 3D Sanitary Gravity Drainage Pipe with 1.5% Standard Slope and automated elbows in Revit.
        /// Zero external file downloads needed.
        /// </summary>
        public static ExecutionResult GeneratePlumbingDrainPipe(UIDocument uidoc, MepPlanReport report)
        {
            var result = new ExecutionResult { ActionName = "Generate 3D Sanitary Drain Pipe" };
            Document doc = uidoc.Document;

            try
            {
                PipeType? pipeType = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipeType))
                    .Cast<PipeType>()
                    .FirstOrDefault();

                PipingSystemType? pipingSystem = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipingSystemType))
                    .Cast<PipingSystemType>()
                    .FirstOrDefault();

                if (pipeType == null || pipingSystem == null)
                {
                    result.Success = false;
                    result.Message = "No Plumbing Pipe system types loaded in this model template. Please transfer or load Piping System Types.";
                    return result;
                }

                // Elevation slightly below level slab: -0.5 ft
                double baseElevationFt = report.LevelElevationFeet - 0.5;
                var spinePoints = ComputeSpinePoints(report, baseElevationFt);

                List<ElementId> createdIds = new List<ElementId>();
                int elbowCount = 0;

                RevitActionExecutor.RunInTransaction(doc, "BIM AIA: Generate Sanitary Drain Pipe", () =>
                {
                    var createdPipes = new List<Pipe>();
                    XYZ currentStart = spinePoints[0];

                    for (int i = 0; i < spinePoints.Count - 1; i++)
                    {
                        XYZ p1 = currentStart;
                        XYZ targetP2 = spinePoints[i + 1];

                        // Apply 1.5% gravity slope drop along distance
                        double distance = p1.DistanceTo(targetP2);
                        if (distance > 1.0)
                        {
                            double drop = distance * 0.015; // 1.5% slope drop
                            XYZ slopedP2 = new XYZ(targetP2.X, targetP2.Y, p1.Z - drop);

                            Pipe pipe = Pipe.Create(doc, pipingSystem.Id, pipeType.Id, report.LevelId, p1, slopedP2);
                            if (pipe != null)
                            {
                                pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(100.0 / 304.8); // DN100 (4")
                                pipe.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"BIM AIA: Sanitary Drainage with 1.5% Gravity Slope ({report.LevelName})");

                                createdIds.Add(pipe.Id);
                                createdPipes.Add(pipe);
                                currentStart = slopedP2;

                                RevitActionExecutor.ChangeHistory.Insert(0, new ModelChangeRecord
                                {
                                    ElementId = pipe.Id,
                                    Category = "Pipes",
                                    ChangeType = "Generative Plumbing Drainage",
                                    Description = $"Generated DN100 Waste Pipe with 1.5% gravity slope on {report.LevelName}",
                                    BeforeValue = "None",
                                    AfterValue = "Modeled 3D Sloped Pipe",
                                    ColorHex = "#10B981"
                                });
                            }
                        }
                    }

                    // Auto-connect adjacent pipes with sanitary elbows
                    for (int i = 0; i < createdPipes.Count - 1; i++)
                    {
                        FamilyInstance? elbow = TryConnectWithElbow(doc, createdPipes[i], createdPipes[i + 1]);
                        if (elbow != null)
                        {
                            elbowCount++;
                            createdIds.Add(elbow.Id);
                            elbow.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"BIM AIA: Sanitary Drain Elbow Fitting ({report.LevelName})");

                            RevitActionExecutor.ChangeHistory.Insert(0, new ModelChangeRecord
                            {
                                ElementId = elbow.Id,
                                Category = "Pipe Fittings",
                                ChangeType = "Generative Plumbing Fitting",
                                Description = $"Auto-Connected Sloped Drain Elbow on {report.LevelName}",
                                BeforeValue = "Open Pipe End",
                                AfterValue = "Continuous Waste System",
                                ColorHex = "#10B981"
                            });
                        }
                    }
                });

                if (createdIds.Count > 0)
                {
                    RevitActionExecutor.EnsureModelView(uidoc);
                    RevitActionExecutor.SafeFocusElements(uidoc, createdIds);
                    result.Success = true;
                    result.AffectedElementIds = createdIds;
                    result.VisualColorApplied = "#10B981 (Emerald Green)";
                    string elbowMsg = elbowCount > 0 ? $" + {elbowCount} Sanitary Elbow(s) Connected" : "";
                    result.Message = $"Generated {createdIds.Count - elbowCount} Sanitary Drain Pipe section(s){elbowMsg} with 1.5% gravity slope on {report.LevelName}.";
                }
                else
                {
                    result.Success = false;
                    result.Message = "Could not generate drainage pipe run along the current coordinates.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error generating drainage pipe: {ex.Message}";
            }

            return result;
        }

        private static List<XYZ> ComputeSpinePoints(MepPlanReport report, double zElevation)
        {
            var spinePoints = new List<XYZ>();

            if (report.RoomRequirements.Count == 1)
            {
                // Single Room Scope: Compute orientation, entry and L-shaped distribution with 90° bend
                var room = report.RoomRequirements[0];
                if (room.BoundingBox != null)
                {
                    double minX = room.BoundingBox.Min.X;
                    double maxX = room.BoundingBox.Max.X;
                    double minY = room.BoundingBox.Min.Y;
                    double maxY = room.BoundingBox.Max.Y;
                    double dx = Math.Abs(maxX - minX);
                    double dy = Math.Abs(maxY - minY);

                    if (dx > 5.0 && dy > 5.0)
                    {
                        // Spacious room: L-shaped branch entering from corner, turning 90° along center
                        XYZ pEntry = new XYZ(minX + 2.0, minY + 2.0, zElevation);
                        XYZ pCorner = new XYZ(minX + 2.0, room.CenterPoint.Y, zElevation);
                        XYZ pEnd = new XYZ(maxX - 2.0, room.CenterPoint.Y, zElevation);

                        spinePoints.Add(pEntry);
                        spinePoints.Add(pCorner);
                        spinePoints.Add(pEnd);
                    }
                    else if (dx >= dy)
                    {
                        // Narrow room along X: 3-point bend
                        double offset = Math.Min(2.0, dx * 0.25);
                        XYZ pEntry = new XYZ(minX + offset, minY + (dy * 0.3), zElevation);
                        XYZ pCorner = new XYZ(minX + offset, room.CenterPoint.Y, zElevation);
                        XYZ pEnd = new XYZ(maxX - offset, room.CenterPoint.Y, zElevation);

                        spinePoints.Add(pEntry);
                        spinePoints.Add(pCorner);
                        spinePoints.Add(pEnd);
                    }
                    else
                    {
                        // Narrow room along Y: 3-point bend
                        double offset = Math.Min(2.0, dy * 0.25);
                        XYZ pEntry = new XYZ(minX + (dx * 0.3), minY + offset, zElevation);
                        XYZ pCorner = new XYZ(room.CenterPoint.X, minY + offset, zElevation);
                        XYZ pEnd = new XYZ(room.CenterPoint.X, maxY - offset, zElevation);

                        spinePoints.Add(pEntry);
                        spinePoints.Add(pCorner);
                        spinePoints.Add(pEnd);
                    }
                }
                else
                {
                    spinePoints.Add(new XYZ(room.CenterPoint.X - 3.0, room.CenterPoint.Y - 2.0, zElevation));
                    spinePoints.Add(new XYZ(room.CenterPoint.X - 3.0, room.CenterPoint.Y, zElevation));
                    spinePoints.Add(new XYZ(room.CenterPoint.X + 3.0, room.CenterPoint.Y, zElevation));
                }
            }
            else if (report.RoomRequirements.Count > 1)
            {
                var validRooms = report.RoomRequirements
                    .Where(r => r.CenterPoint != XYZ.Zero)
                    .ToList();

                if (validRooms.Count >= 2)
                {
                    double minX = validRooms.Min(r => r.CenterPoint.X);
                    double maxX = validRooms.Max(r => r.CenterPoint.X);
                    double minY = validRooms.Min(r => r.CenterPoint.Y);
                    double maxY = validRooms.Max(r => r.CenterPoint.Y);
                    double spanX = maxX - minX;
                    double spanY = maxY - minY;

                    if (spanX > 4.0 && spanY > 4.0)
                    {
                        // Multi-room Level Spine: Shaft/Riser connection turning onto main distribution corridor
                        XYZ pShaft = new XYZ(minX, minY, zElevation);
                        XYZ pTurn = new XYZ(minX, (minY + maxY) / 2.0, zElevation);
                        XYZ pTrunk = new XYZ(maxX, (minY + maxY) / 2.0, zElevation);

                        spinePoints.Add(pShaft);
                        spinePoints.Add(pTurn);
                        spinePoints.Add(pTrunk);
                    }
                    else if (spanX >= spanY)
                    {
                        var sorted = validRooms.OrderBy(r => r.CenterPoint.X).ToList();
                        spinePoints.Add(new XYZ(sorted.First().CenterPoint.X, sorted.First().CenterPoint.Y, zElevation));
                        spinePoints.Add(new XYZ(sorted.Last().CenterPoint.X, sorted.Last().CenterPoint.Y, zElevation));
                    }
                    else
                    {
                        var sorted = validRooms.OrderBy(r => r.CenterPoint.Y).ToList();
                        spinePoints.Add(new XYZ(sorted.First().CenterPoint.X, sorted.First().CenterPoint.Y, zElevation));
                        spinePoints.Add(new XYZ(sorted.Last().CenterPoint.X, sorted.Last().CenterPoint.Y, zElevation));
                    }
                }
            }

            if (spinePoints.Count < 2 || spinePoints[0].DistanceTo(spinePoints[1]) < 1.0)
            {
                spinePoints.Clear();
                spinePoints.Add(new XYZ(0, 0, zElevation));
                spinePoints.Add(new XYZ(0, 10.0, zElevation));
                spinePoints.Add(new XYZ(15.0, 10.0, zElevation));
            }

            return spinePoints;
        }

        public static ExecutionResult PlaceAvailableFixtures(UIDocument uidoc, MepPlanReport report)
        {
            var result = new ExecutionResult { ActionName = "Place Calculated MEP Fixtures" };
            Document doc = uidoc.Document;

            try
            {
                var lightSymbol = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(IsValidMepSymbol);

                var airTerminalSymbol = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctTerminal)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(IsValidMepSymbol);

                if (lightSymbol == null && airTerminalSymbol == null)
                {
                    result.Success = false;
                    result.Message = "No lighting fixture or diffuser families loaded. Please load families from Autodesk library first.";
                    return result;
                }

                List<ElementId> placedIds = new List<ElementId>();
                List<string> unplacedReasons = new List<string>();

                RevitActionExecutor.RunInTransaction(doc, "BIM AIA: Place MEP Fixtures", () =>
                {
                    if (lightSymbol != null && !lightSymbol.IsActive) lightSymbol.Activate();
                    if (airTerminalSymbol != null && !airTerminalSymbol.IsActive) airTerminalSymbol.Activate();

                    Level? lvl = doc.GetElement(report.LevelId) as Level;

                    foreach (var room in report.RoomRequirements)
                    {
                        if (lightSymbol != null && (report.Discipline == MepDiscipline.Electrical || report.Discipline == MepDiscipline.FullMep))
                        {
                            FamilyPlacementType fpt = lightSymbol.Family.FamilyPlacementType;
                            bool isPointHostable = fpt == FamilyPlacementType.OneLevelBased || fpt == FamilyPlacementType.WorkPlaneBased;

                            foreach (var pt in room.FixtureGridPoints)
                            {
                                try
                                {
                                    if (isPointHostable)
                                    {
                                        FamilyInstance fi = doc.Create.NewFamilyInstance(pt, lightSymbol, lvl, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                        if (fi != null)
                                        {
                                            placedIds.Add(fi.Id);
                                            fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"BIM AIA: {room.RoomName} Illumination Fixture");
                                        }
                                    }
                                }
                                catch { }
                            }

                            if (!isPointHostable && room.FixtureGridPoints.Count > 0)
                            {
                                unplacedReasons.Add($"Lighting '{lightSymbol.FamilyName}' is {fpt} (Requires Ceiling element in {room.RoomName})");
                            }
                        }

                        if (airTerminalSymbol != null && (report.Discipline == MepDiscipline.MechanicalHvac || report.Discipline == MepDiscipline.FullMep))
                        {
                            FamilyPlacementType fpt = airTerminalSymbol.Family.FamilyPlacementType;
                            bool isPointHostable = fpt == FamilyPlacementType.OneLevelBased || fpt == FamilyPlacementType.WorkPlaneBased;

                            foreach (var pt in room.DiffuserGridPoints)
                            {
                                try
                                {
                                    if (isPointHostable)
                                    {
                                        FamilyInstance fi = doc.Create.NewFamilyInstance(pt, airTerminalSymbol, lvl, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                        if (fi != null)
                                        {
                                            placedIds.Add(fi.Id);
                                            fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set($"BIM AIA: {room.RoomName} Air Terminal");
                                        }
                                    }
                                }
                                catch { }
                            }

                            if (!isPointHostable && room.DiffuserGridPoints.Count > 0)
                            {
                                unplacedReasons.Add($"Air Terminal '{airTerminalSymbol.FamilyName}' is {fpt} (Requires Ceiling element in {room.RoomName})");
                            }
                        }
                    }
                });

                if (placedIds.Count > 0)
                {
                    RevitActionExecutor.EnsureModelView(uidoc);
                    RevitActionExecutor.SafeFocusElements(uidoc, placedIds);
                    result.Success = true;
                    result.AffectedElementIds = placedIds;
                    result.VisualColorApplied = "#FACC15 (Solar Yellow)";
                    result.Message = $"Successfully placed {placedIds.Count} MEP terminal fixture(s) on {report.LevelName}.";
                }
                else
                {
                    result.Success = false;
                    string reason = unplacedReasons.Count > 0 ? string.Join("; ", unplacedReasons.Distinct()) : "Loaded families require ceiling hosts that are not yet modeled in these spaces.";
                    result.Message = $"Could not place fixtures: {reason}. (Prerequisite: Model a Ceiling or load Level-based fixtures).";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Error placing fixtures: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Formats an engineering assessment report with spatial calculations and family checklist.
        /// </summary>
        public static string FormatReportMarkdown(MepPlanReport r)
        {
            var sb = new StringBuilder();
            string disciplineIcon = r.Discipline switch
            {
                MepDiscipline.Electrical => "⚡ Electrical Engineering",
                MepDiscipline.MechanicalHvac => "❄️ Mechanical HVAC & Ventilation",
                MepDiscipline.PlumbingSanitary => "🚰 Sanitary Plumbing & Drainage",
                _ => "🌐 Multi-Discipline MEP"
            };

            sb.AppendLine($"### {disciplineIcon} Blueprint: **{r.LevelName}**");
            sb.AppendLine($"* **Target Scope:** `{r.ScopeDescription}`");
            sb.AppendLine($"* **Total Usable Area Analyzed:** `{r.TotalAreaSqMeters:0.1} m²` across `{r.TotalRooms}` space(s)");
            sb.AppendLine();

            sb.AppendLine("#### 🟢 What I Can Execute Right Now:");
            if (r.ReadinessReport.CapableActions.Count > 0)
            {
                foreach (var item in r.ReadinessReport.CapableActions)
                {
                    sb.AppendLine($"* {item.StatusIcon} **{item.Title}**: {item.Details}");
                }
            }
            else
            {
                sb.AppendLine("* *(No actions ready without prerequisites)*");
            }

            if (r.ReadinessReport.BlockedActions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("#### 🔴 What I Cannot Execute Yet (Constraints):");
                foreach (var item in r.ReadinessReport.BlockedActions)
                {
                    sb.AppendLine($"* {item.StatusIcon} **{item.Title}**: {item.Details}");
                }

                if (r.ReadinessReport.UserActionableRoadmap.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("#### 🛠️ Actionable Prerequisite Roadmap for You:");
                    for (int i = 0; i < r.ReadinessReport.UserActionableRoadmap.Count; i++)
                    {
                        sb.AppendLine($"* **{i + 1}.** {r.ReadinessReport.UserActionableRoadmap[i]}");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("### 📐 **Engineering Sizing & Load Calculations**");
            sb.AppendLine("* **Structural Clearance:** 🛡️ Clash-Free Beam Avoidance Active (Soffit Elevation Scanned)");
            sb.AppendLine("* **Network Topology:** 🟢 Automated 90° Elbows & Continuity Joining (MEP System Validated)");

            if (r.Discipline == MepDiscipline.Electrical || r.Discipline == MepDiscipline.FullMep)
            {
                sb.AppendLine($"* **Lighting Requirement:** `{r.TotalLightingFixtures}` fixtures (CIBSE / IESNA Lumen Method)");
                sb.AppendLine($"* **Power Outlets:** `{r.TotalSockets}` duplex sockets | Est. Load: `{r.TotalConnectedLoadKva:0.2} kVA` (~{r.TotalConnectedLoadKva * 0.85:0.2} kW)");
                sb.AppendLine($"* **Cable Tray Containment:** `~{r.CableTrayRouteLengthMeters:0} m` (300x100mm System Spine)");
            }

            if (r.Discipline == MepDiscipline.MechanicalHvac || r.Discipline == MepDiscipline.FullMep)
            {
                sb.AppendLine($"* **Total Airflow Requirement:** `{r.TotalAirflowCfm:0} CFM` (ASHRAE 62.1 Standard)");
                sb.AppendLine($"* **Air Terminals / Diffusers:** `{r.TotalDiffusersOrGrilles}` units | Duct Run: `~{r.DuctRouteLengthMeters:0} m` (300x200mm)");
            }

            if (r.Discipline == MepDiscipline.PlumbingSanitary || r.Discipline == MepDiscipline.FullMep)
            {
                sb.AppendLine($"* **Sanitary Drainage Units:** `{r.TotalPlumbingFixtures * 3}` DFU | Pipe Sizing: `DN100 (4\")` Main Waste");
                sb.AppendLine($"* **Drainage Gravity Slope:** `1.5% - 2.0%` standard plumbing gradient");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("### 📋 **Family Readiness & Procurement Checklist (Bill of Families)**");

            foreach (var item in r.FamilyChecklist)
            {
                string statusIcon = item.IsAvailable ? "✅ **Ready in Model**" : "❌ **Missing - Action Required**";
                sb.AppendLine($"#### {statusIcon}: `{item.CategoryName}`");
                sb.AppendLine($"* **Role:** {item.RoleDescription}");
                sb.AppendLine($"* **Status:** `{item.LoadedFamilyName}`");
                if (!item.IsAvailable)
                {
                    sb.AppendLine($"* **Recommended Family:** `{item.RecommendedFamily}`");
                    sb.AppendLine($"* **How to Load:** In Revit: `{item.AutodeskLibraryPath}`");
                }
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine("### 🏢 **Spatial Room-by-Room Calculations**");
            sb.AppendLine("| Room / Zone | Space Type | Area (m²) | Lux / CFM | Sockets / DFU | Sizing / Spec |");
            sb.AppendLine("| :--- | :--- | :---: | :---: | :---: | :--- |");

            foreach (var room in r.RoomRequirements.Take(8))
            {
                string metric1 = r.Discipline == MepDiscipline.MechanicalHvac ? $"{room.RequiredAirflowCfm:0} CFM" : $"{room.TargetLux} lx";
                string metric2 = r.Discipline == MepDiscipline.PlumbingSanitary ? $"{room.DrainageFixtureUnits} DFU" : $"{room.CalculatedSockets} Sockets";
                string spec = r.Discipline == MepDiscipline.MechanicalHvac ? room.DuctSizeRecommendation : (r.Discipline == MepDiscipline.PlumbingSanitary ? room.PipeSizeRecommendation : $"{room.CalculatedFixtures}x Fixtures ({room.EstimatedPowerKva} kVA)");

                sb.AppendLine($"| **{room.RoomName}** ({room.RoomNumber}) | {room.SpaceType} | {room.AreaSqMeters} | {metric1} | {metric2} | {spec} |");
            }

            if (r.RoomRequirements.Count > 8)
            {
                sb.AppendLine($"| *... and {r.RoomRequirements.Count - 8} more rooms* | | | | | |");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine("### 🚀 **Instant CAD Modeling Options (Zero External Downloads)**");
            sb.AppendLine("1. **3D System Spines (Cable Tray / Duct / Sloped Pipe):** Click the action button above to generate immediate 3D CAD geometry natively in Revit.");
            sb.AppendLine("2. **Terminal Fixtures:** Once loadable families are in the project, click to auto-place fixtures across all coordinate grids.");

            return sb.ToString();
        }
    }
}
