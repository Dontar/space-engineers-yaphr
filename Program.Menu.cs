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
                var mainMenu = new Menu("Crane control") {
                    new OptionItem { Label = "Configuration >", Action = (menu, index) => BuildConfigMenu() },
                    new OptionItem { Label = "PID controls >", Action = (menu, index) => BuildPidControlsMenu() },
                    new OptionItem { Label = "Profile", Value = (m,j) => program.Profile, IncDec = (m, j, d) => {
                        var sections = new List<string>();
                        program.Config.GetSections(sections);
                        var allProfiles = sections.Select(s => s.Split('/')[0]).Distinct().ToArray();
                        program.Profile = allProfiles[(Array.IndexOf(allProfiles, program.Profile) + d + (d < 0 ? allProfiles.Length : 0)) % allProfiles.Length];
                    }},
                    new OptionItem { Label = "Mode", Value = (m, j) => program.Mode.ToString(), Action = (m, j) => program.ProcessCommands("toggle_mode") },
                    new OptionItem { Label = "Park", Action = (menu, index) => program.ProcessCommands("park") },
                    new OptionItem { Label = "Work", Action = (menu, index) => program.ProcessCommands("work") },
                    new OptionItem { Label = "Set Park position", Action = (menu, index) => program.ProcessCommands("set_park") },
                    new OptionItem { Label = "Set Work position", Action = (menu, index) => program.ProcessCommands("set_work") },
                };

                menuStack.Push(mainMenu);
            }

            void BuildConfigMenu()
            {
                Action<Menu, int> toggleCheckBox = (m, j) =>
                {
                    m[j].Label = m[j].Label.StartsWith("[ ]") ? m[j].Label.Replace("[ ]", "[x]") : m[j].Label.Replace("[x]", "[ ]");
                    m[1].Label = ".Save";
                };
                Action<Menu, int> save = (m, j) =>
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
                };

                var configMenu = new Menu("Configuration") {
                    new OptionItem { Label = "< Back", Action = (m, j) => { menuStack.Pop(); } },
                    new OptionItem { Label = "Save", Action = save },
                };

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
                    .Except(configuredGroups.Select(g => g.Split('/').Last()).Distinct());
                foreach (var name in list)
                {
                    configMenu.Add(new OptionItem { Label = $"[ ] {name}", Action = toggleCheckBox });
                }
                menuStack.Push(configMenu);
            }

            void BuildPidControlsMenu()
            {
                var blocksMenu = new Menu("PID Controls") { new OptionItem { Label = "< Back", Action = (_, i) => { menuStack.Pop(); } } };
                program.Sections.ForEach(info =>
                {
                    blocksMenu.Add(new OptionItem { Label = info.section + " >", Action = (_, i) => BuildPidControlsSubMenu(info) });
                });
                menuStack.Push(blocksMenu);
            }

            void BuildPidControlsSubMenu(PIDDescriptor info)
            {
                var step = 1;
                var pidMenu = new Menu(info.section) {
                    new OptionItem { Label = "< Back", Action = (m, j) => { menuStack.Pop(); }},
                    new OptionItem { Label = "Save", Action = (m, j) => {
                        var myIni = program.Config;
                        info.UpdateIni(program.Profile, myIni);
                        program.Me.CustomData = myIni.ToString();
                        m[1].Label = "Save";
                    }},

                    new OptionItem { Label = "Control input", Value = (m, j) => info.op, IncDec = (m, j, d) => {
                        info.op = InputNames[(Array.IndexOf(InputNames, info.op) + d + (d < 0 ? InputNames.Length : 0)) % InputNames.Length];
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Control gain",  Value = (m, j) => info.desiredVelocity.ToString(), IncDec = (m, j, d) => {
                        info.desiredVelocity += -d;
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Step",          Value = (m, j) => $"1 / {step}", IncDec = (m, j, d) => {
                        step = Math.Max(1, step + -d);
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Kp",            Value = (m, j) => info.tune[0].ToString(), IncDec = (m, j, d) => {
                        info.tune[0] += -d / step;
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Ki",            Value = (m, j) => info.tune[1].ToString(), IncDec = (m, j, d) => {
                        info.tune[1] += -d / step;
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Kd",            Value = (m, j) => info.tune[2].ToString(), IncDec = (m, j, d) => {
                        info.tune[2] += -d / step;
                        m[1].Label = ".Save";
                    }},

                    new OptionItem { Label = "Decay",         Value = (m, j) => info.tune[3].ToString(), IncDec = (m, j, d) => {
                        info.tune[3] += -d / step;
                        m[1].Label = ".Save";
                    }},
                };
                menuStack.Push(pidMenu);
            }
        }
    }
}
