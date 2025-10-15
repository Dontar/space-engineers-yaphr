using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class CraneControlMenuManager : MenuManager
        {
            public CraneControlMenuManager(Program program) : base(program) {
                var mainMenu = CreateMenu("Crane control");
                mainMenu.AddArray(new OptionItem[] {
                    new OptionItem { Label = "Configuration >", Action = (menu, index) => BuildPidControlsMenu() },
                    new OptionItem { Label = "Profile", Value = (m,j) => program.Profile, IncDec = (m, j, d) => {
                        var sections = new List<string>();
                        program.Config.GetSections(sections);
                        var allProfiles = sections.Select(s => s.Split('/')[0]).Distinct().ToArray();
                        program.Profile = allProfiles[(Array.IndexOf(allProfiles, program.Profile) + d + (d < 0 ? allProfiles.Length : 0)) % allProfiles.Length];
                    }},
                    new OptionItem { Label = "Mode", Value = (m, j) => program.Mode, Action = (m, j) => program.ProcessCommands("toggle_mode") },
                    new OptionItem { Label = "KeepAlign", Value = (_, __) => program.KeepAlign.ToString()},
                    new OptionItem { Label = "Park", Action = (menu, index) => program.ProcessCommands("park") },
                    new OptionItem { Label = "Work", Action = (menu, index) => program.ProcessCommands("work") },
                    new OptionItem { Label = "Set Park position", Action = (menu, index) => program.ProcessCommands("set_park") },
                    new OptionItem { Label = "Set Work position", Action = (menu, index) => program.ProcessCommands("set_work") },
                });
            }

            class BlockNamesComparer : EqualityComparer<IMyTerminalBlock>
            {
                public override bool Equals(IMyTerminalBlock x, IMyTerminalBlock y) => x.CustomName == y.CustomName;
                public override int GetHashCode(IMyTerminalBlock obj) => obj.CustomName.GetHashCode();
            }

            void BuildPidControlsMenu() {
                var blocksMenu = CreateMenu("Configuration");
                var gts = program.GridTerminalSystem;

                var blocks = new List<IMyTerminalBlock>();
                gts.GetBlockGroupWithName(craneGroup)?.GetBlocksOfType<IMyMechanicalConnectionBlock>(blocks, b => !(b is IMyMotorSuspension));

                if (blocks.Count == 0) {
                    blocksMenu.Add(new OptionItem { Label = "-- No blocks found!!! --" });
                    return;
                }

                var result = blocks
                .Distinct(new BlockNamesComparer())
                .GroupJoin(program.Sections, o => o.CustomName, i => i.Section, (o, i) => {
                    var wrapper = i.SingleOrDefault();
                    if (wrapper != null) return wrapper;
                    var tempIni = new Dictionary<string, string>();
                    Array.ForEach(InputNames, iName => tempIni.Add(iName, "0"));
                    tempIni.Add("Tuning", "0/15/0/2");
                    return new PistonMotorWrapper(o.CustomName, tempIni);
                });

                blocksMenu.AddArray(result.Select(info => new OptionItem { Label = info.Section + " >", Action = (_, i) => BuildPidControlsSubMenu(info) }).ToArray());
            }

            void BuildPidControlsSubMenu(PistonMotorWrapper info) {
                var step = 1;
                var pidMenu = CreateMenu(info.Section);
                pidMenu.AddArray(new OptionItem[] {
                    new OptionItem { Label = "Save", Action = (m, j) => {
                        var myIni = program.Config;
                        info.UpdateIni(program.Profile, myIni);
                        program.Me.CustomData = myIni.ToString();
                        m[1].Label = "Save";
                    }},

                    new OptionItem { Label = "Align To", Value = (m, j) => info.KeepAlignedTo.ToString(), IncDec = (m, j, d) => {
                        info.KeepAlignedTo = Directions[(Array.IndexOf(Directions, info.KeepAlignedTo) + d + (d < 0 ? Directions.Length : 0)) % Directions.Length];
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Control input", Value = (m, j) => info.OP, IncDec = (m, j, d) => {
                        info.OP = InputNames[(Array.IndexOf(InputNames, info.OP) + d + (d < 0 ? InputNames.Length : 0)) % InputNames.Length];
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Control gain",  Value = (m, j) => info.DesiredVelocity.ToString(), IncDec = (m, j, d) => {
                        info.DesiredVelocity += -d;
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Step",          Value = (m, j) => $"1 / {step}", IncDec = (m, j, d) => {
                        step = Math.Max(1, step + -d);
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Kp",            Value = (m, j) => info.PIDTune[0].ToString(), IncDec = (m, j, d) => {
                        info.PIDTune[0] += -d / step;
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Ki",            Value = (m, j) => info.PIDTune[1].ToString(), IncDec = (m, j, d) => {
                        info.PIDTune[1] += -d / step;
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Kd",            Value = (m, j) => info.PIDTune[2].ToString(), IncDec = (m, j, d) => {
                        info.PIDTune[2] += -d / step;
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Decay",         Value = (m, j) => info.PIDTune[3].ToString(), IncDec = (m, j, d) => {
                        info.PIDTune[3] += -d / step;
                        m[1].Label = ".Save";
                    }},
                });
            }
        }
    }
}
