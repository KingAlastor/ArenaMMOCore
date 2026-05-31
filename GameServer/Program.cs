using System;
using System.Diagnostics;
using System.Threading;
using SharedLibrary;

class Program {
    private const float TargetTickRate = 30f;
    private const float MSPerTick = 1000f / TargetTickRate;

    static void Main(string[] args) {
        Console.WriteLine("Game Server Initializing...");
    
        bool isRunning = true;
        Stopwatch ticktimer = new Stopwatch();
        double accumaltion = 0;

        Console.WriteLine("Server active at 30Hz.");

        while (isRunning) {
            ticktimer.Restart();

            UpdateServerTick();

            double elapsed = ticktimer.Elapsed.TotalMilliseconds;
            if (elapsed < MSPerTick) {
                Thread.Sleep((int)(MSPerTick - elapsed));
            }

        }
    }

    static void UpdateServerTick() {

    }
} 