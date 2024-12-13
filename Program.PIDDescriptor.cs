using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript
{
    partial class Program
    {
        class PIDDescriptor : PID
        {
            public List<IMyTerminalBlock> Blocks;
            public Dictionary<string, string> INI;
            public string OP;
            public double DesiredVelocity = 0;
            public double[] PIDTune;
            public string Section;
            public PIDDescriptor(string section, Dictionary<string, string> ini) : base(0, 0, 0, 1d / 6d, 0)
            {
                var op = ini.FirstOrDefault(o => InputNames.Contains(o.Key) && o.Value != "0");

                Blocks = Util.GetGroupOrBlocks<IMyTerminalBlock>(section).ToList();
                INI = ini;
                Section = section;
                OP = op.Key ?? "None";
                double.TryParse(op.Value, out DesiredVelocity);

                PIDTune = ini.GetValueOrDefault("Tuning", "0/15/0/2").Split('/').Select(double.Parse).ToArray();
                Tune(PIDTune);
            }
            public void UpdateIni(string Profile, MyIni ini)
            {
                INI.Remove(OP);
                INI.Add(OP, DesiredVelocity.ToString());
                INI.ToList().ForEach(k =>
                {
                    ini.DeleteSection(Section);
                    ini.Set($"{Profile}/{Section}", k.Key, InputNames.Contains(k.Key) && k.Key != OP ? "0" : k.Value);
                });
                ini.Set($"{Profile}/{Section}", "Tuning", string.Join("/", PIDTune));
            }
        }
    }
}
