using System;
using System.Diagnostics.Tracing;
using System.Net.Security;
using System.Threading;
using System.Xml.Schema;
using System.Drawing;
using ScottPlot;

namespace TPProj
{
    class Program
    {
        static Random rnd = new Random();
        static void Main()
        {
            Console.WriteLine("Запуск эксперимента СМО: ");
            double mu = 20.0;
            int threadsCount = 3;
            int totalRequests = 3000;

            List<double> lambdas = new List<double>();
            double minL = 0.5 * mu;
            double maxL = 3.0 * mu;
            for (int i = 0; i < 15; i++)
            {
                double l = minL + (maxL - minL) * i / 14.0;
                lambdas.Add(l);
            }
            Console.WriteLine($"Потоков: {threadsCount}, интенсивность обслуживания: {mu} заявок в секунду, заявок на точку: {totalRequests}, точек: {lambdas.Count}");
            Directory.CreateDirectory("result");
            using (StreamWriter w = new StreamWriter("result/results.txt", false, System.Text.Encoding.UTF8))
            {
                w.WriteLine("lambda\tP0_teor\tPn_teor\tQ_teor\tA_teor\tk_teor\tP0_exp\tPn_exp\tQ_exp\tA_exp\tk_exp");
                List<double> xs = new List<double>();
                List<double> p0t = new List<double>(), pnt = new List<double>(), qt = new List<double>(), at = new List<double>(), kt = new List<double>();
                List<double> p0e = new List<double>(), pne = new List<double>(), qe = new List<double>(), ae = new List<double>(), ke = new List<double>();
                foreach(double l in lambdas)
                {
                    var teor = CalculateStatistics.Calculate(threadsCount, l, mu);
                    var exp = RunExperiment(threadsCount, l, mu, totalRequests);
                    xs.Add(l);
                    p0t.Add(teor.P0); pnt.Add(teor.Pn); qt.Add(teor.Q); at.Add(teor.A); kt.Add(teor.k);
                    p0e.Add(exp.P0); pne.Add(exp.Pn); qe.Add(exp.Q); ae.Add(exp.A); ke.Add(exp.k);
                    Console.WriteLine($"{l:F2}\t{teor.P0:F4}\t{teor.Pn:F4}\t{teor.Q:F4}\t{teor.A:F2}\t{teor.k:F4}\t{exp.P0:F4}\t{exp.Pn:F4}\t{exp.Q:F4}\t{exp.A:F2}\t{exp.k:F4}");
                    w.WriteLine($"{l:F2}\t{teor.P0:F4}\t{teor.Pn:F4}\t{teor.Q:F4}\t{teor.A:F2}\t{teor.k:F4}\t{exp.P0:F4}\t{exp.Pn:F4}\t{exp.Q:F4}\t{exp.A:F2}\t{exp.k:F4}");
                    Thread.Sleep(500);
                }
                MakePlot(xs, p0t, p0e, "Вероятность простоя системы P0", "result/p-1.png");
                MakePlot(xs, pnt, pne, "Вероятность отказа Pn", "result/p-2.png");
                MakePlot(xs, qt, qe, "Относительная пропускная способность Q", "result/p-3.png");
                MakePlot(xs, at, ae, "Абсолютная пропускная способность A", "result/p-4.png");
                MakePlot(xs, kt, ke, "Среднее число занятых каналов k", "result/p-5.png");
                Console.WriteLine("\nГрафики сохранены в папке result/");
            }
        }
        static ExperimentResult RunExperiment(int threadsCount, double lambda, double mu, int totalRequests)
        {
            Server server = new Server(threadsCount, mu);
            Client client = new Client(server);
            server.MeasureBusyThreads();

            for (int id = 1; id <= totalRequests; id++)
            {
                client.send(id);
                double intervalMs = -1000.0 / lambda * Math.Log(1.0 - Program.rnd.NextDouble());
                Thread.Sleep((int)intervalMs);
                server.MeasureBusyThreads();

                if (id % 500 == 0)
                {
                    Console.WriteLine($"    lambda={lambda:F2}: {id}/{totalRequests}, отказов={server.rejectedCount}");
                }
            }
            Thread.Sleep(5000);

            server.MeasureBusyThreads();
            double p0 = server.GetProbabilityAllFree();
            double pn = (double)server.rejectedCount / server.requestCount;
            double q = (double)server.processedCount / server.requestCount;
            double a = q * lambda;
            double k = server.GetAverageBusyThreads();
            if (p0 < 0) p0 = 0;
            return new ExperimentResult { P0 = p0, Pn = pn, Q = q, A = a, k = k }; 
        }
        static void MakePlot(List<double> x, List<double> yt, List<double> ye, string title, string file)
        {
            var plt = new ScottPlot.Plot(800, 600);
            plt.AddScatter(x.ToArray(), yt.ToArray(), color: Color.Green, lineWidth: 2, markerSize: 0, label: "Теория");
            plt.AddScatter(x.ToArray(), ye.ToArray(), color: Color.Orange, lineWidth: 2, markerSize: 0, label: "Эксперимент");
            plt.Title(title);
            plt.XLabel("Интенсивность входного потока lambda (заявок/сек)");
            plt.YLabel(title);
            plt.Legend(location: Alignment.UpperRight);
            plt.SaveFig(file);
            Console.WriteLine($" {file}");
        }
    }
    struct ExperimentResult
    {
        public double P0, Pn, Q, A, k;
    }
    struct PoolRecord
    {
        public Thread thread;
        public bool in_use;
    }

    class Server
    {
        private double mu;
        private Random rnd = new Random();
        private PoolRecord[] pool;
        private object threadLock = new object();
        public int requestCount = 0;
        public int processedCount = 0;
        public int rejectedCount = 0;
        public int TimeForProcessing;
        private int totalBusyThreads = 0;
        private int CountMeasurements = 0;
        private int countAllFree = 0;
        public Server(int threadsCount, double mu)
        {
            this.mu = mu;
            this.TimeForProcessing = (int)(1000.0 / mu);
            pool = new PoolRecord[threadsCount];
            for (int i = 0; i < threadsCount; i++)
            {
                pool[i].in_use = false;
            }
        }
        public void proc (object sender, procEventArgs e)
        {
            lock (threadLock)
            {
                requestCount++;
                for (int i = 0; i < pool.Length; i++)
                {
                    if (!pool[i].in_use)
                    {
                        pool[i].in_use = true;
                        pool[i].thread = new Thread(new ParameterizedThreadStart(Answer));
                        pool[i].thread.Start(e.id);
                        processedCount++;
                        return;
                    }
                }
                rejectedCount++;
            }
        }
        public void Answer (object arg)
        {
            int id = (int)arg;
            double processingTimeSec = -Math.Log(1.0 - rnd.NextDouble()) / mu;
            Thread.Sleep((int)(processingTimeSec * 1000));

            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i].thread == Thread.CurrentThread)
                {
                    pool[i].in_use = false;
                    break;
                }
            }
        }
        public void MeasureBusyThreads()
        {
            lock (threadLock)
            {
                int busy = 0;
                for (int i = 0; i < pool.Length; i++)
                {
                    if (pool[i].in_use)
                    {
                        busy++;
                    }
                }
                totalBusyThreads += busy;
                CountMeasurements++;
                if (busy == 0) countAllFree++;
            }
        }
        public double GetProbabilityAllFree()
        {
            if (CountMeasurements == 0) return 0;
            return (double)countAllFree / CountMeasurements;
        }
        public double GetAverageBusyThreads()
        {
            if (CountMeasurements == 0) return 0;
            return (double)totalBusyThreads / CountMeasurements;
        }
    }
    class Client
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
            {
                handler(this, e);
            }
        }
        public event EventHandler<procEventArgs> request;
    }
    public class procEventArgs: EventArgs
    {
        public int id { get; set; }
    }
    static class CalculateStatistics
    {
        static double Factorial(int n)
        {
            double result = 1;
            for (int i = 2; i <= n; i++)
            {
                result *= i;
            }
            return result;
        }
        public static (double P0, double Pn, double Q, double A, double k) Calculate(int n, double lambda, double mu)
        {
            double ro = lambda / mu;
            double sumP_0 = 0;
            for (int i = 0; i <= n; i++)
            {
                sumP_0 += Math.Pow(ro, i) / Factorial(i);
            }
            double P0 = 1.0/sumP_0;
            double Pn = (Math.Pow(ro, n) / Factorial(n))*P0;
            double Q = 1 - Pn;
            double A = lambda * Q;
            double k = A / mu;
            return (P0, Pn, Q, A, k);
        }
    }
}
