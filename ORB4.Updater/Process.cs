using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ORB4.Updater
{
    class Operation
    {
        public string Description { get; set; }
        public string Name { get; set; }
        public Func<Task> Main { get; set; }
    }

    abstract class Process
    {
        public bool Running { get; set; }

        public List<Action> RollbackOperations { get; set; } = new List<Action>();
        public List<Operation> Operations { get; set; }

        public string CurrentDescription { get; set; } = "Install";

        internal int _previousPercentage = 0;
        public int Percentage { get; set; } = 0;

        public string Path { get; set; }

        public abstract Task Clear();

        public System.Threading.CancellationTokenSource CancellationTokenSource { get; private set; } = new System.Threading.CancellationTokenSource();

        public async Task Start()
        {
            Running = true;
            foreach (var op in Operations)
            {
                if (Running)
                {
                    Console.WriteLine(op.Name);
                    CurrentDescription = op.Description;
                    _previousPercentage = Percentage;
                    var result = Task.Factory.StartNew(() => op.Main.Invoke().GetAwaiter().GetResult());
                    await result;
                }
                else
                    break;
            }

            await Task.Delay(1000);

            await Clear();

            Running = false;
            OnInstallationFinish.Invoke(this, new EventArgs());
        }

        public void AddRollbackOperation(Action action)
        {
            RollbackOperations.Add(action);
        }

        public event EventHandler<EventArgs> OnInstallationFinish;
        public event EventHandler<EventArgs> OnInstallationError;

        public async Task OperationsRollback()
        {
            try
            {
                if (RollbackOperations.Count == 0)
                    return;

                int increment = 10000 / RollbackOperations.Count;
                Percentage = 0;

                for (int i = RollbackOperations.Count - 1; i > -1; i--)
                {
                    Percentage += increment;

                    try
                    {
                        CurrentDescription = "Operations rollback...";

                        Console.WriteLine("RLB#" + i);
                        RollbackOperations[i].Invoke();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                        continue;
                    }

                    await Task.Delay(1);
                }

                Percentage = 10000;
            }
            catch (Exception e)
            {
                Environment.Exit(e.HResult);
            }
        }
    }
}
