﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace IngameScript
{
    partial class Program
    {
        public class PID
        {
            public double Kp = 0;
            public double Ki = 0;
            public double Kd = 0;
            public double Decay = 0;

            double previousError = 0;
            double errorAccumulator = 0;
            double deltaTime = 0;
            bool _firstRun = true;

            public PID(double kp, double ki, double kd, double deltaTime, double id = 0) {
                Tune(new double[] { kp, ki, kd, id });
                this.deltaTime = deltaTime;
            }


            double I(double error) {
                errorAccumulator *= 1d - Decay;
                errorAccumulator += error * deltaTime;// += e(t) * dt
                return Ki * errorAccumulator;
            }

            double D(double error) {
                double errorDerivative = (error - previousError) / deltaTime;// de(t) / dt = (e(t) - e(t-1)) / dt
                previousError = error;
                if (_firstRun) {
                    errorDerivative = 0;
                    _firstRun = false;
                }

                return Kd * errorDerivative;
            }

            public double Signal(double error) {
                return Kp * error + I(error) + D(error);
            }

            public double Signal(double error, double deltaTime, double[] tune = null) {
                if (tune != null) Tune(tune);
                this.deltaTime = deltaTime;
                return Signal(error);
            }

            public void Tune(double[] tune) {
                Kp = tune[0] / 10d;
                Ki = tune[1] / 10d;
                Kd = tune[2] / 1000d;
                Decay = tune[3] / 10d;
            }

            public void Reset() {
                previousError = 0;
                errorAccumulator = 0;
                _firstRun = true;
            }
        }
    }
}
