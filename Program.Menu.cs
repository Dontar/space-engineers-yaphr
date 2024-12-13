using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Utils;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class CraneControlMenuManager : MenuManager
        {
            public CraneControlMenuManager(Program program) : base(program)
            {
                var mainMenu = CreateMenu("Crane control");
                mainMenu.AddArray(new OptionItem[] {
                    new OptionItem { Label = "Configuration >", Action = (menu, index) => BuildConfigMenu() },
                    new OptionItem { Label = "PID controls >", Action = (menu, index) => BuildPidControlsMenu() },
                    new OptionItem { Label = "Profile", Value = (m,j) => program.Profile, IncDec = (m, j, d) => {
                        var sections = new List<string>();
                        program.Config.GetSections(sections);
                        var allProfiles = sections.Select(s => s.Split('/')[0]).Distinct().ToArray();
                        program.Profile = allProfiles[(Array.IndexOf(allProfiles, program.Profile) + d + (d < 0 ? allProfiles.Length : 0)) % allProfiles.Length];
                    }},
                    new OptionItem { Label = "Mode", Value = (m, j) => program.Mode, Action = (m, j) => program.ProcessCommands("toggle_mode") },
                    new OptionItem { Label = "Park", Action = (menu, index) => program.ProcessCommands("park") },
                    new OptionItem { Label = "Work", Action = (menu, index) => program.ProcessCommands("work") },
                    new OptionItem { Label = "Set Park position", Action = (menu, index) => program.ProcessCommands("set_park") },
                    new OptionItem { Label = "Set Work position", Action = (menu, index) => program.ProcessCommands("set_work") },
                });
            }

            void BuildConfigMenu()
            {
                var configMenu = CreateMenu("Configuration");
                Action<Menu, int> toggleCheckBox = (m, j) =>
                {
                    m[j].Label = m[j].Label.StartsWith("[ ]") ? m[j].Label.Replace("[ ]", "[x]") : m[j].Label.Replace("[x]", "[ ]");
                    m[1].Label = ".Save";
                };

                configMenu.Add(new OptionItem { Label = "Save", Action = (m, j) =>
                {
                    var myIni = program.Config;
                    m.Where(s => s.Label.StartsWith("[x]"))
                        .ToList()
                        .ForEach(s =>
                        {
                            var section = s.Label.Substring(4);
                            InputNames.ToList().ForEach(i => myIni.Set(section, i, "0"));
                            myIni.Set($"{program.Profile}/{section}", "Tuning", "0/15/0/2");
                        });
                    program.Me.CustomData = myIni.ToString();
                    m[1].Label = "Save";
                } });

                var groups = new List<IMyBlockGroup>();
                program.GridTerminalSystem.GetBlockGroups(groups);
                var groupBlocks = groups.SelectMany(g =>
                {
                    var b = new List<IMyTerminalBlock>();
                    g.GetBlocks(b);
                    return b;
                });

                var configuredGroups = new List<string>();
                program.Config.GetSections(configuredGroups);

                var allBlocks = new List<IMyTerminalBlock>();
                program.GridTerminalSystem.GetBlocksOfType(allBlocks, b => b is IMyExtendedPistonBase || b is IMyMotorStator);

                var list = groups
                    .Select(g => g.Name)
                    .Concat(allBlocks.Except(groupBlocks).Select(b => b.CustomName).Distinct())
                    .Except(configuredGroups.Select(g => g.Split('/').Last()).Distinct())
                    .Select(name => new OptionItem { Label = $"[ ] {name}", Action = toggleCheckBox }).ToArray();

                configMenu.AddArray(list);
            }

            void BuildPidControlsMenu()
            {
                var blocksMenu = CreateMenu("PID Controls");
                blocksMenu.AddArray(program.Sections.Select(info => new OptionItem { Label = info.Section + " >", Action = (_, i) => BuildPidControlsSubMenu(info) }).ToArray());
            }

            void BuildPidControlsSubMenu(PIDDescriptor info)
            {
                var step = 1;
                var pidMenu = CreateMenu(info.Section);
                pidMenu.AddArray(new OptionItem[] {
                    new OptionItem { Label = "Save", Action = (m, j) => {
                        var myIni = program.Config;
                        info.UpdateIni(program.Profile, myIni);
                        program.Me.CustomData = myIni.ToString();
                        m[1].Label = "Save";
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
