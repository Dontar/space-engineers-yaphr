using Sandbox.Game.EntityComponents;
using Sandbox.Game.VoiceChat;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using VRageRender.ExternalApp;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        const string RotationIndicatorY = "Yaw";
        const string RotationIndicatorX = "Pitch";
        const string RollIndicator = "Roll";
        const string MoveIndicatorX = "Left/Right";
        const string MoveIndicatorY = "Up/Down";
        const string MoveIndicatorZ = "Forward/Backward";
        const string screenTag = "[CC]";

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            Util.Init(this);
            menuSystem = new CraneControlMenuManager(this);
            TaskManager.AddTask(Util.DisplayLogo("Y.A.P.H.R", Me.GetSurface(0)));
            TaskManager.AddTask(ControlCrane());
            TaskManager.AddTask(PositionCrane("Park"));
            TaskManager.AddTask(PositionCrane("Work"));
            TaskManager.AddTask(RenderToScreensTask(), 1.7f);
        }

        private IEnumerable RenderToScreensTask()
        {
            while (true)
            {
                Screens.ForEach(s => menuSystem.Render(s));
                yield return null;
            }
        }

        private readonly CraneControlMenuManager menuSystem;
        private string Mode = "off";
        private string Profile = "default";

        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                ProcessCommands(argument);
                if (updateSource.HasFlag(UpdateType.Update10))
                {
                    TaskManager.RunTasks(Runtime.TimeSinceLastRun);
                }
            }
            catch (Exception e)
            {
                Log(e.ToString());
            }
        }

        void Log(string msg)
        {
            Echo(msg);
        }

        public static readonly string[] InputNames = new string[] { RotationIndicatorX, RotationIndicatorY, RollIndicator, MoveIndicatorX, MoveIndicatorY, MoveIndicatorZ };

        IEnumerable ControlCrane()
        {
            while (true)
            {
                if (Mode == "control")
                {
                    Sections.ForEach(info =>
                    {
                        if (info.blocks.Count == 0) return;
                        var controllers = Memo.Of(() => Util.GetBlocks<IMyShipController>(b => b.CubeGrid == Me.CubeGrid && b.CanControlShip), "ControlCrane", 100);
                        var direction = ReadControllerValue(controllers.FirstOrDefault(c => c.IsUnderControl), info.op);
                        var block = info.blocks.First();
                        var velocityState = Descriptor.Get(block);
                        var targetVelocity = MathHelper.Clamp(info.desiredVelocity, -velocityState.Max, velocityState.Max);
                        var error = targetVelocity * direction - velocityState.Current;
                        var output = info.Signal(error, TaskManager.CurrentTaskLastRun.TotalSeconds, info.tune);
                        info.blocks.ForEach(b => Descriptor.Set(b, (float)output));

                    });
                };
                yield return null;
            }
        }

        IEnumerable PositionCrane(string optName)
        {
            while (true)
            {
                if (Mode == optName.ToLower())
                {
                    Sections.ForEach(info =>
                    {
                        if (!info.ini.ContainsKey(optName) || info.blocks.Count == 0) return;
                        var value = info.ini[optName].Split('/');
                        var desiredPos = value.Skip(4).Select(float.Parse).FirstOrDefault();
                        var tune = value.Take(4).Select(double.Parse).ToArray();
                        var block = info.blocks.First();
                        var descriptor = Descriptor.Get(block);
                        var error = (block is IMyMotorStator) ? MathHelper.WrapAngle(desiredPos - descriptor.Position) : desiredPos - descriptor.Position;
                        var output = info.Signal(error, TaskManager.CurrentTaskLastRun.TotalSeconds, tune);
                        info.blocks.ForEach(b => Descriptor.Set(b, (float)output));
                    });
                }
                yield return null;
            }
        }

        IEnumerable SavePositions(string optName)
        {
            Sections.ForEach(info =>
            {
                if (info.blocks.Count == 0) return;
                var ini = Config;
                var descriptor = Descriptor.Get(info.blocks.First());
                ini.Set($"{Profile}/{info.section}", optName, $"15/{info.tune[1]}/{info.tune[2]}/{info.tune[3]}/{descriptor.Position}");
            });
            Me.CustomData = Config.ToString();
            yield return null;
        }

        float ReadControllerValue(IMyShipController controller, string name)
        {
            if (controller == null) return 0;
            switch (name)
            {
                case MoveIndicatorX:
                    return controller.MoveIndicator.X;
                case MoveIndicatorY:
                    return controller.MoveIndicator.Y;
                case MoveIndicatorZ:
                    return controller.MoveIndicator.Z;
                case RollIndicator:
                    return controller.RollIndicator;
                case RotationIndicatorY:
                    return controller.RotationIndicator.Y;
                case RotationIndicatorX:
                    return controller.RotationIndicator.X;
                default:
                    return 0;
            }
        }

        void ProcessCommands(string command)
        {
            Action stop = () => Sections.ForEach(i =>
            {
                i.Reset();
                i.blocks.ForEach(b => Descriptor.Set(b, 0f));
            });

            if (command == null || command.Length == 0) return;
            switch (command.ToLower())
            {
                case "toggle":
                    stop();
                    Mode = Mode != "off" ? "off" : "control";
                    break;
                case "toggle_park":
                case "toggle_mode":
                    stop();
                    Mode = Mode == "control" ? "park" : Mode == "park" ? "work" : "control";
                    break;
                case "set_park":
                case "set_work":
                    TaskManager.AddTaskOnce(SavePositions(char.ToUpper(command[4]) + command.Substring(5).ToLower()));
                    break;
                case "park":
                case "work":
                    stop();
                    Mode = command.ToLower();
                    break;
                default:
                    if (command.ToLower().StartsWith("add"))
                    {
                        AddProfile(command.Substring(4));
                    }
                    if (command.ToLower().StartsWith("set"))
                    {
                        Profile = command.Substring(4);
                    }
                    if (menuSystem.ProcessMenuCommands(command))
                    {
                        Screens.ForEach(s => menuSystem.Render(s));
                    }
                    break;
            }
        }

        private void AddProfile(string v)
        {
            var opts = new List<MyIniKey>();
            Config.GetKeys(opts);

            var p = opts.Select(k =>
            {
                var t = k.Section.Split('/');
                var profile = t.Length < 2 ? "default" : t.First();
                var section = t.Last();
                return new { k.Name, Section = section, Profile = profile, Value = Config.Get(k).ToString() };
            });

            if (p.Where(k => k.Profile == v).Any()) return;

            p.Where(k => k.Profile == "default")
            .ToList()
            .ForEach(i =>
            {
                Config.Set($"{v}/{i.Section}", i.Name, i.Value);
                Me.CustomData = Config.ToString();
            });
        }

        List<IMyTextSurface> Screens => Memo.Of(() => Util.GetScreens(screenTag), "Screens", 100);

        List<PIDDescriptor> Sections => Memo.Of(() =>
            {
                var opts = new List<MyIniKey>();
                Config.GetKeys(opts);
                return opts.Select(k =>
                {
                    var p = k.Section.Split('/');
                    var profile = p.Length < 2 ? "default" : p.First();
                    var section = p.Last();
                    return new { k.Name, Section = section, Profile = profile, Value = Config.Get(k).ToString() };
                })
                .Where(i => i.Profile == Profile)
                .GroupBy(
                    iniKey => iniKey.Section,
                    theIniKey => theIniKey,
                    (section, theIniKey) => new PIDDescriptor(section, theIniKey.ToDictionary(k => k.Name, k => k.Value)))
                .ToList();
            }, "Sections", Memo.Refs(Config, Profile));
        MyIni Config => Memo.Of(() =>
            {
                var myIni = new MyIni();
                myIni.TryParse(Me.CustomData);
                return myIni;
            }, "GetConfig", Memo.Refs(Me.CustomData));
    }
}
