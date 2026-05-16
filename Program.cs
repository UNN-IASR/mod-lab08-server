using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using ScottPlot;
namespace Lab8
{
    public class procEventArgs : EventArgs
    {
        public int id { get; set; }
    }
    public class Values
    {
        public double lambda;
        public double Q;
        public double A;
        public double mu;
        public double P_0;
        public double P_n;
        public double k;
    }
    public class Server
    {
        private struct PoolRecord
        {
            public Thread thread;
            public bool engaged;
        }
        private PoolRecord[] pool;
        private object threadLock = new object();
        public int channels;
        public int freeAmount = 0;
        public int totalAmount = 0;
        public int requests = 0;
        public int done = 0;
        public int outcasts = 0;
        public long totalTime = 0;
        public int timeCounter = 0;
        public double avgTime = 0;
        private int serviceTime;
        public Server(int n, int serviceTime = 50)
        {
            channels = n;
            this.serviceTime = serviceTime;
            pool = new PoolRecord[n];
            for (int i = 0; i < n; i++)
                pool[i].engaged = false;
        }
        public void Reset()
        {
            requests = 0;
            done = 0;
            outcasts = 0;
            freeAmount = 0;
            totalAmount = 0;
            totalTime = 0;
            timeCounter = 0;
            avgTime = 0;
        }
        public void CheckFree()
        {
            lock (threadLock)
            {
                totalAmount++;
                bool allFree = true;
                for (int i = 0; i < channels; i++)
                {
                    if (pool[i].engaged)
                    {
                        allFree = false;
                        break;
                    }
                }
                if (allFree) freeAmount++;
            }
        }
        public void proc(object sender, procEventArgs e)
        {
            lock (threadLock)
            {
                requests++;
                for (int i = 0; i < channels; i++)
                {
                    if (!pool[i].engaged)
                    {
                        pool[i].engaged = true;
                        pool[i].thread = new Thread(new ParameterizedThreadStart(Answer));
                        pool[i].thread.Start(e.id);
                        done++;
                        return;
                    }
                }
                outcasts++;
            }
        }
        private void Answer(object arg)
        {
            int id = (int)arg;
            DateTime start = DateTime.Now;
            Thread.Sleep(serviceTime);
            DateTime end = DateTime.Now;
            double ms = (end - start).TotalMilliseconds;
            lock (threadLock)
            {
                totalTime += (long)ms;
                timeCounter++;
                avgTime = (double)totalTime / timeCounter;
                for (int i = 0; i < channels; i++)
                {
                    if (pool[i].thread == Thread.CurrentThread)
                    {
                        pool[i].engaged = false;
                        break;
                    }
                }
            }
        }
    }
    public class Client
    {
        private Server server;
        public Client(Server server)
        {
            this.server = server;
            this.request += server.proc;
        }
        public void send(int id)
        {
            procEventArgs args = new procEventArgs();
            args.id = id;
            OnProc(args);
        }
        protected virtual void OnProc(procEventArgs e)
        {
            EventHandler<procEventArgs> handler = request;
            if (handler != null)
                handler(this, e);
        }
        public event EventHandler<procEventArgs> request;
    } 
    internal class Program()
    {
        static int Factorial(int n)
        {
            int result = 1;
            for (int i = 2; i <= n; i++)
                result *= i;
            return result;
        }
        static Values Calculate(double lambda, double mu, int n)
        {
            Values val = new Values();
            val.lambda = lambda;
            val.mu = mu;
            double rho = lambda / mu;
            double sum = 0;
            for (int k = 0; k <= n; k++)
                sum += Math.Pow(rho, k) / Factorial(k);
            val.P_0 = 1.0 / sum;
            val.P_n = (Math.Pow(rho, n) / Factorial(n)) * val.P_0;
            val.Q = 1 - val.P_n;
            val.A = lambda * val.Q;
            val.k = val.A / mu;
            return val;
        }
        static void DrawGraph(double[] x, double[] yTheory, double[] yExp, string xLabel, string yLabel, string title, string filename)
        {
            var plot = new Plot();
            plot.XLabel(xLabel);
            plot.YLabel(yLabel);
            plot.Title(title);
            var theory = plot.Add.Scatter(x, yTheory);
            theory.Label = "Тheory";
            theory.Color = new ScottPlot.Color(0, 0, 255);
            var experiment = plot.Add.Scatter(x, yExp);
            experiment.Label = "Actual";
            experiment.Color = new ScottPlot.Color(255, 0, 0);
            plot.ShowLegend();
            plot.SavePng(filename, 1920, 1080);
        }
        static void Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string? projectDir = Directory.GetParent(baseDir)?.Parent?.Parent?.FullName;
            string? solutionDir = Directory.GetParent(projectDir)?.FullName;

            if (solutionDir == null)
            {
                throw new InvalidOperationException("Не удалось определить путь к решению");
            }

            string resultDir = Path.Combine(solutionDir, "result");
            Directory.CreateDirectory(resultDir);
            string path = resultDir;
            int[] delays = { 100, 150, 210, 250 };
            int channels = 3;
            int applications = 100;
            int serviceTime = 50;
            double mu = (double) 1000 / serviceTime;
            List<double> lambda = new List<double>();
            List<double> P_0Theory = new List<double>();
            List<double> P_nTheory = new List<double>(); 
            List<double> QTheory = new List<double>();        
            List<double> ATheory = new List<double>();
            List<double> kTheory = new List<double>();
            List<double> P_0Actual = new List<double>();
            List<double> P_nActual = new List<double>();
            List<double> QActual = new List<double>();
            List<double> AActual = new List<double>();
            List<double> kActual = new List<double>();
            string path2 = Path.Combine(path, "results.txt");
            StreamWriter file = new StreamWriter(path2, false, System.Text.Encoding.UTF8);
            file.WriteLine($"Channels: {channels}, Applications: {applications}, Mu = {mu}\n");
            file.WriteLine("lam\tP0th\t\tP0act\tPth\t\tPact\tQth\t\tQact\tAth\t\tAact\tkth\t\tkact");
            file.WriteLine("***********************************************************************************");
            Console.WriteLine("Building the graphs...");
            for (int i = 0; i < delays.Length; i++)
            {
                int currentDelay = delays[i];
                double lambdaValue = 1000.0 / currentDelay;
                Server server = new Server(channels, serviceTime);
                Client client = new Client(server);
                DateTime start = DateTime.Now;
                for (int id = 1; id <= applications; id++)
                {
                    client.send(id);
                    Thread.Sleep(currentDelay);
                    server.CheckFree();
                }
                DateTime end = DateTime.Now;
                double span = (end - start).TotalSeconds;
                Values data = new Values();
                data.lambda = server.requests / span;
                data.mu = 1000.0 / server.avgTime;
                data.P_0 = (double)server.freeAmount / server.totalAmount;
                data.P_n = (double)server.outcasts / server.requests;
                data.Q = 1 - data.P_n;
                data.A = data.lambda * data.Q;
                data.k = data.A / data.mu;
                Values theor = Calculate(lambdaValue, mu, channels);
                lambda.Add(lambdaValue);
                P_0Actual.Add(data.P_0); P_0Theory.Add(theor.P_0);
                P_nActual.Add(data.P_n); P_nTheory.Add(theor.P_n);
                QActual.Add(data.Q); QTheory.Add(theor.Q);
                AActual.Add(data.A); ATheory.Add(theor.A);
                kActual.Add(data.k); kTheory.Add(theor.k);
                file.WriteLine($"{lambda:F2}\t\t{theor.P_0:F4}\t{data.P_0:F4}\t\t{theor.P_n:F4}\t{data.P_n:F4}\t\t{theor.Q:F4}\t{data.Q:F4}\t\t{theor.A:F2}\t{data.A:F2}\t\t{theor.k:F2}\t{data.k:F2}");
            }
            double[] x = lambda.ToArray();
            DrawGraph(x, P_0Theory.ToArray(), P_0Actual.ToArray(),"Lambda intensity", "Downtime probability P0", "P0(lambda)", Path.Combine(path, "p-1.png"));
            DrawGraph(x, P_nTheory.ToArray(), P_nActual.ToArray(),"Lambda intensity", "Refusal probability P_отказ", "P_отказ(lambda)", Path.Combine(path, "p-2.png"));
            DrawGraph(x, QTheory.ToArray(), QActual.ToArray(),"Lambda intensity", "Relative throughput capacity Q", "Q(lambda)", Path.Combine(path, "p-3.png"));
            DrawGraph(x, ATheory.ToArray(), AActual.ToArray(),"Lambda intensity", "Absolute throughput capacity A", "A(lambda)", Path.Combine(path, "p-4.png"));
            DrawGraph(x, kTheory.ToArray(), kActual.ToArray(),"Lambda intensity", "Average amount of engaged channels k","k(lambda)", Path.Combine(path, "p-5.png"));
            file.Close();
            Console.WriteLine("\nAll graphs have been done as files.\n");
        }
    }
}
