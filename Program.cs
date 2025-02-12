﻿using Sandbox.Game.EntityComponents;
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
            TaskManager.AddTask(RenderToScreensTask(), 1.7f);
            _ControlTask = TaskManager.AddTask(ControlCrane());
            _ParkTask = TaskManager.AddTask(PositionCrane("Park"));
            _WorkTask = TaskManager.AddTask(PositionCrane("Work"));
            _ControlTask.IsPaused = true;
            _ParkTask.IsPaused = true;
            _WorkTask.IsPaused = true;
        }

        readonly CraneControlMenuManager menuSystem;

        string Mode = "off";
        string Profile = "default";
        readonly TaskManager.Task _LogoTask;
        readonly TaskManager.Task _ControlTask;
        readonly TaskManager.Task _ParkTask;
        readonly TaskManager.Task _WorkTask;

        public void Main(string argument, UpdateType updateSource)
        {
            ProcessCommands(argument);

            if (!updateSource.HasFlag(UpdateType.Update10)) return;

            if (Controllers.Count() == 0)
            {
                Log("No controller found.");
                return;
            }

            try
            {
                TaskManager.RunTasks(Runtime.TimeSinceLastRun);
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
            var sections = Sections;
            while (sections.Equals(Sections))
            {
                var controller = Controllers.FirstOrDefault(c => c.IsUnderControl);
                sections.ForEach(descriptor =>
                {
                    if (descriptor.Blocks.Count == 0) return;
                    var direction = ReadControllerValue(controller, descriptor.OP);
                    var block = descriptor.Blocks.First();
                    var velocityState = Descriptor.Get(block);
                    var targetVelocity = MathHelper.Clamp(descriptor.DesiredVelocity, -velocityState.Max, velocityState.Max);
                    var error = targetVelocity * direction - velocityState.Current;
                    var output = descriptor.Signal(error, TaskManager.CurrentTaskLastRun.TotalSeconds, descriptor.PIDTune);
                    descriptor.Blocks.ForEach(b => Descriptor.Set(b, (float)Math.Round(output, 3)));
                });
                yield return null;
            }
        }

        IEnumerable PositionCrane(string optName)
        {
            var sections = Sections;
            while (sections.Equals(Sections))
            {
                sections.ForEach(descriptor =>
                {
                    if (!descriptor.INI.ContainsKey(optName) || descriptor.Blocks.Count == 0) return;
                    var value = descriptor.INI[optName].Split('/');
                    var desiredPos = value.Skip(4).Select(float.Parse).FirstOrDefault();
                    var tune = value.Take(4).Select(double.Parse).ToArray();
                    var block = descriptor.Blocks.First();
                    var positionState = Descriptor.Get(block);
                    var error = (block is IMyMotorStator) ? MathHelper.WrapAngle(desiredPos - positionState.Position) : desiredPos - positionState.Position;
                    var output = descriptor.Signal(error, TaskManager.CurrentTaskLastRun.TotalSeconds, tune);
                    descriptor.Blocks.ForEach(b => Descriptor.Set(b, (float)Math.Round(output, 3)));
                });
                yield return null;
            }
        }

        IEnumerable SavePositions(string optName)
        {
            Sections.ForEach(info =>
            {
                if (info.Blocks.Count == 0) return;
                var ini = Config;
                var descriptor = Descriptor.Get(info.Blocks.First());
                ini.Set($"{Profile}/{info.Section}", optName, $"15/{info.PIDTune[1]}/{info.PIDTune[2]}/{info.PIDTune[3]}/{descriptor.Position}");
            });
            Me.CustomData = Config.ToString();
            yield return null;
        }

        private IEnumerable RenderToScreensTask()
        {
            while (true)
            {
                foreach (var s in Screens)
                {
                    if (s == Me.GetSurface(0))
                    {
                        _LogoTask.IsPaused = true;
                    }
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
            if (command == null || command.Length == 0) return;
            Sections.ForEach(s =>
            {
                s.Reset();
                s.Blocks.ForEach(b => Descriptor.Set(b, 0f));
            });
            switch (command.ToLower())
            {
                case "toggle":
                    Mode = Mode != "control" ? "control" : "off";
                    break;
                case "toggle_park":
                case "toggle_mode":
                    Mode = Mode == "control" ? "park" : Mode == "park" ? "work" : "control";
                    break;
                case "set_park":
                case "set_work":
                    TaskManager.AddTaskOnce(SavePositions(char.ToUpper(command[4]) + command.Substring(5).ToLower()));
                    break;
                case "park":
                case "work":
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
                        foreach (var s in Screens) menuSystem.Render(s);
                    }
                    break;
            }
            _ControlTask.IsPaused = Mode != "control";
            _ParkTask.IsPaused = Mode != "park";
            _WorkTask.IsPaused = Mode != "work";
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

        IEnumerable<IMyShipController> Controllers => Memo.Of(() => Util.GetBlocks<IMyShipController>(b => b.CubeGrid == Me.CubeGrid && b.CanControlShip), "ControlCrane", 100);

        IEnumerable<IMyTextSurface> Screens => Memo.Of(() => Util.GetScreens(screenTag), "Screens", 100);

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
