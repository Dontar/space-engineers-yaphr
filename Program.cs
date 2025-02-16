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
        #region mdk preserve
        const string RotationIndicatorY = "Yaw";
        const string RotationIndicatorX = "Pitch";
        const string RollIndicator = "Roll";
        const string MoveIndicatorX = "Left/Right";
        const string MoveIndicatorY = "Up/Down";
        const string MoveIndicatorZ = "Forward/Backward";
        const string screenTag = "[CC]";
        #endregion

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            Util.Init(this);
            menuSystem = new CraneControlMenuManager(this);
            _LogoTask = TaskManager.AddTask(Util.DisplayLogo("Y.A.P.H.R", Me.GetSurface(0)));
            TaskManager.AddTask(RenderMenu(), 1.7f);
            _ControlTask = TaskManager.AddTask(ControlCrane());
            _ParkTask = TaskManager.AddTask(PositionCrane("Park"));
            _WorkTask = TaskManager.AddTask(PositionCrane("Work"));
            TaskManager.AddTask(Util.StatusMonitor(this));
            _ControlTask.IsPaused = true;
            _ParkTask.IsPaused = true;
            _WorkTask.IsPaused = true;
        }

        readonly CraneControlMenuManager menuSystem;

        string Mode = "off";
        string Profile = "default";
        TaskManager.Task _LogoTask;
        TaskManager.Task _ControlTask;
        TaskManager.Task _ParkTask;
        TaskManager.Task _WorkTask;

        public void Main(string argument, UpdateType updateSource)
        {
            if (!string.IsNullOrEmpty(argument))
                ProcessCommands(argument);

            if (!updateSource.HasFlag(UpdateType.Update10)) return;

            if (Controllers.Count() == 0)
            {
                Util.Echo("No controller found.");
                return;
            }

            _ControlTask.IsPaused = Mode != "control";
            _ParkTask.IsPaused = Mode != "park";
            _WorkTask.IsPaused = Mode != "work";

            TaskManager.RunTasks(Runtime.TimeSinceLastRun);
        }

        IEnumerable ControlCrane()
        {
            while (true)
            {
                var controller = Controllers.FirstOrDefault(c => c.IsUnderControl);
                foreach (var descriptor in Sections)
                {
                    var direction = ReadControllerValue(controller, descriptor.OP);
                    descriptor.Control(direction);
                }
                yield return null;
            }
        }

        IEnumerable PositionCrane(string position)
        {
            while (true)
            {
                if (Sections.Select(d => d.Position(position)).All(d => d))
                {
                    Mode = "off";
                    Stop();
                }
                yield return null;
            }
        }

        IEnumerable SavePositions(string optName)
        {
            foreach (var info in Sections)
            {
                if (info.Blocks.Length == 0) continue;
                var ini = Config;
                var descriptor = Descriptor.Get(info.Blocks.First());
                ini.Set($"{Profile}/{info.Section}", optName, $"15/{info.PIDTune[1]}/{info.PIDTune[2]}/{info.PIDTune[3]}/{descriptor.Position}");
            }
            Me.CustomData = Config.ToString();
            yield return null;
        }

        private IEnumerable RenderMenu()
        {
            while (true)
            {
                foreach (var s in Screens)
                {
                    if (s == Me.GetSurface(0)) _LogoTask.IsPaused = true;
                    menuSystem.Render(s);
                }
                yield return null;
            }
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
            var cmd = command.ToLower().Trim();
            Stop();
            switch (cmd)
            {
                case "toggle":
                    Mode = Mode != "control" ? "control" : "off";
                    break;
                case "set_park":
                case "set_work":
                    TaskManager.AddTaskOnce(SavePositions(char.ToUpper(cmd[4]) + cmd.Substring(5)));
                    break;
                case "park":
                case "work":
                    Mode = cmd;
                    break;
                default:
                    var match = cmd.Substring(0, 3);
                    switch (match)
                    {
                        case "add":
                            AddProfile(command.Substring(4).Trim());
                            break;
                        case "set":
                            Profile = command.Substring(4).Trim();
                            break;
                        default:
                            if (menuSystem.ProcessMenuCommands(command)) foreach (var s in Screens) menuSystem.Render(s);
                            break;
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

            p.Where(k => k.Profile == "default").ToList().ForEach(i =>
            {
                Config.Set($"{v}/{i.Section}", i.Name, i.Value);
                Me.CustomData = Config.ToString();
            });
        }

        IEnumerable<IMyShipController> Controllers => Memo.Of(() => Util.GetBlocks<IMyShipController>(b => b.CubeGrid == Me.CubeGrid && b.CanControlShip), "ControlCrane", 100);

        IEnumerable<IMyTextSurface> Screens => Memo.Of(() => Util.GetScreens(screenTag), "Screens", 100);

        IEnumerable<PIDDescriptor> Sections => Memo.Of(() =>
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
                .ToArray();
            }, "Sections", Memo.Refs(Config, Profile));
        MyIni Config => Memo.Of(() =>
            {
                var myIni = new MyIni();
                myIni.TryParse(Me.CustomData);
                return myIni;
            }, "GetConfig", Memo.Refs(Me.CustomData));

        void Stop()
        {
            foreach (var s in Sections)
            {
                s.Reset();
                Array.ForEach(s.Blocks, b => Descriptor.Set(b, 0f));
            }
        }
    }
}
