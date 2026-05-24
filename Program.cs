using ScottPlot;
using System;
using System.Diagnostics;
using System.Threading;
namespace TPProj
{
    class Program
    {
        static void Main(string[] args)
        {
            Random rand = new Random();

            double mu = 2.0;  

            int totalRequests = 100;
            int requestHandleTime = 500;
            int poolSize = 5;
            double[] lambdaValues = { 2, 4, 6, 8, 10, 12, 14, 16, 18, 20 };

            List<double> theorP0 = [];
            List<double> theorPR = [];
            List<double> theorQ = [];
            List<double> theorA = [];
            List<double> theorK = [];

            List<double> expP0 = [];
            List<double> expPR = [];
            List<double> expQ = [];
            List<double> expA = [];
            List<double> expK = [];

            string projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\.."));
            string resultDir = Path.Combine(projectRoot, "result");
            if (!Directory.Exists(resultDir))
                Directory.CreateDirectory(resultDir);

            string resultPath = Path.Combine(resultDir, "result.txt");

            using (StreamWriter sw = new StreamWriter(resultPath))
            {
                sw.WriteLine("lambda, theorP0, expP0, theorPR, expPR, theorQ, expQ, theorA, expA, theorK, expK");
            }

            foreach (double lambda in lambdaValues)
            {
                double requestTimeInterval = 1000.0 / lambda;

                Console.WriteLine($"\n=== Эксперимент: lambda = {lambda:F2} заявок/сек ===");

                Stopwatch sw_total = new Stopwatch();
                sw_total.Start();

                Server server = new Server(poolSize, requestHandleTime, sw_total);
                Client client = new Client(server);

                for (int id = 1; id <= totalRequests; id++)
                {
                    client.send(id);
                    double interval = -Math.Log(1.0 - rand.NextDouble()) / lambda;
                    int ms = (int)(interval * 1000);
                    if (ms > 0) Thread.Sleep(ms);
                }

                server.WaitForCompletion();
                server.FinalizeIdleMeasurement();
                sw_total.Stop();

                double totalTimeSec = sw_total.ElapsedMilliseconds / 1000.0;
                double totalIdleSec = server.GetTotalIdleTime() / 1000.0;
                double totalBusySec = server.processedCount * requestHandleTime / 1000.0;

                double valExpPR = (double)server.rejectedCount / totalRequests;
                double valExpQ = 1.0 - valExpPR;
                double valExpA = server.processedCount / totalTimeSec; 
                double valExpK = totalBusySec / totalTimeSec; 
                double valExpP0 = totalIdleSec / totalTimeSec;  

                double rho = lambda / mu;
                double sum = 0.0;
                for (int j = 0; j <= 5; j++)
                {
                    sum += Math.Pow(rho, j) / Factorial(j);
                }
                double valTheorP0 = 1.0 / sum;
                double valTheorPR = (Math.Pow(rho, poolSize) / Factorial(poolSize)) / sum; 
                double valTheorQ = 1.0 - valTheorPR;  
                double valTheorA = lambda * valTheorQ;
                double valTheorK = valTheorA / mu;

                theorP0.Add(valTheorP0);
                theorPR.Add(valTheorPR);
                theorQ.Add(valTheorQ);
                theorA.Add(valTheorA);
                theorK.Add(valTheorK);

                expP0.Add(valExpP0);
                expPR.Add(valExpPR);
                expQ.Add(valExpQ);
                expA.Add(valExpA);
                expK.Add(valExpK);

                Console.WriteLine($"Всего заявок: {server.requestCount}");
                Console.WriteLine($"Обслужено: {server.processedCount}, Отклонено: {server.rejectedCount}");
                Console.WriteLine($"Общее время: {totalTimeSec:F2} с, Время простоя всех каналов: {totalIdleSec:F2} с");
                Console.WriteLine("\n--- Экспериментальные показатели ---");
                Console.WriteLine($"P0 (простой) = {valExpP0:F6}");
                Console.WriteLine($"Pотк = {valExpPR:F6}");
                Console.WriteLine($"Q = {valExpQ:F6}");
                Console.WriteLine($"A = {valExpA:F3} заявок/с");
                Console.WriteLine($"k = {valExpK:F3} каналов");
                Console.WriteLine("\n--- Теоретические показатели (формулы Эрланга) ---");
                Console.WriteLine($"P0 = {valTheorP0:F6}");
                Console.WriteLine($"Pотк = {valTheorPR:F6}");
                Console.WriteLine($"Q = {valTheorQ:F6}");
                Console.WriteLine($"A = {valTheorA:F3} заявок/с");
                Console.WriteLine($"k = {valTheorK:F3} каналов");

                using (StreamWriter sWriter = new StreamWriter(resultPath, true))
                {
                    sWriter.WriteLine($"{lambda:F4}, {valTheorP0:F4}, {valExpP0:F4}, {valTheorPR:F4}, {valExpPR:F4}, {valTheorQ:F4}, {valExpQ:F4}, {valTheorA:F4}, {valExpA:F4}, {valTheorK:F4}, {valExpK:F4}");
                }
            }

            Console.WriteLine($"\n\nРезультаты сохранены в файл {resultPath}");

            CreatePlot(lambdaValues, [.. expP0], [.. theorP0], "Вероятность", "Вероятность простоя системы", Path.Combine(resultDir, "p-1.png"));
            CreatePlot(lambdaValues, [.. expPR], [.. theorPR], "Вероятность", "Вероятность отказа системы", Path.Combine(resultDir, "p-2.png"));
            CreatePlot(lambdaValues, [.. expQ], [.. theorQ], "Вероятность", "Относительная пропускная способность", Path.Combine(resultDir, "p-3.png"));
            CreatePlot(lambdaValues, [.. expA], [.. theorA], "Вероятность", "Абсолютная пропускная способность", Path.Combine(resultDir, "p-4.png"));
            CreatePlot(lambdaValues, [.. expK], [.. theorK], "Вероятность", "Среднее число занятых каналов", Path.Combine(resultDir, "p-5.png"));
        }

        static double Factorial(int n)
        {
            double f = 1;
            for (int i = 2; i <= n; i++) f *= i;
            return f;
        }

        static void CreatePlot(double[] x, double[] yExp, double[] yTheor, string yLabel,
            string title, string filename)
        {
            ScottPlot.Plot plot = new();
            plot.Title(title);
            plot.XLabel("λ (интенсивность поступления требований, заявок/с)");
            plot.YLabel("Значение");
            var exp = plot.Add.Scatter(x, yExp);
            exp.LegendText = "Экспериментые значения";
            var th = plot.Add.Scatter(x, yTheor);
            th.LegendText = "Теоретические значения";
            plot.Legend.Alignment = ScottPlot.Alignment.LowerRight;
            plot.SavePng(filename, 800, 500);
        }
    }
    struct PoolRecord
    {
        public Thread thread;
        public bool in_use;
    }
    class Server
    {
        private PoolRecord[] pool;
        private object threadLock = new object();
        public int requestCount = 0;
        public int processedCount = 0;
        public int rejectedCount = 0;

        public int requestHandleTime {  get; private set; }
        public int poolSize { get; private set; }

        private int busyChannels = 0;
        private long idleStartTick = 0;
        private long totalIdleTicks = 0;
        private object idleLock = new object();
        private Stopwatch stopwatch;

        public Server(int poolSize, int requestHandleTime, Stopwatch stopwatch)
        {
            pool = new PoolRecord[poolSize];
            this.poolSize = poolSize;
            this.requestHandleTime = requestHandleTime;
            this.stopwatch = stopwatch;

            lock (idleLock)
            {
                busyChannels = 0;
                idleStartTick = stopwatch.ElapsedTicks;
            }
        }
        public void proc(object sender, procEventArgs e)
        {
            lock (threadLock)
            {
                Console.WriteLine("Заявка с номером: {0}", e.id);
                requestCount++;
                for (int i = 0; i < poolSize; i++)
                {
                    if (!pool[i].in_use)
                    {
                        pool[i].in_use = true;
                        pool[i].thread = new Thread(new ParameterizedThreadStart(Answer));
                        pool[i].thread.Start(e.id);
                        processedCount++;

                        lock (idleLock)
                        {
                            if (busyChannels == 0)
                            {
                                long now = stopwatch.ElapsedTicks;
                                totalIdleTicks += now - idleStartTick;
                            }
                            busyChannels++;
                        }

                        return;
                    }
                }
                rejectedCount++;
            }
        }
        public void Answer(object arg)
        {
            int id = (int)arg;
            Console.WriteLine("Обработка заявки: {0}", id);
            Thread.Sleep(requestHandleTime);
            for (int i = 0; i < poolSize; i++)
                if (pool[i].thread == Thread.CurrentThread)
                    pool[i].in_use = false;
            lock (idleLock)
            {
                if (busyChannels == 0)
                {
                    long now = stopwatch.ElapsedTicks;
                    totalIdleTicks += now - idleStartTick;
                }
                busyChannels--;
                if (busyChannels == 0) idleStartTick = stopwatch.ElapsedTicks;
            }
        }
        public void WaitForCompletion()
        {
            bool allFree;
            do
            {
                allFree = true;
                lock (threadLock)
                {
                    foreach (var record in pool)
                        if (record.in_use)
                        {
                            allFree = false;
                            break;
                        }
                }
                if (!allFree)
                    Thread.Sleep(100);
            } while (!allFree);
        }
        public void FinalizeIdleMeasurement()
        {
            lock (idleLock)
            {
                if (busyChannels == 0)
                {
                    long now = stopwatch.ElapsedTicks;
                    totalIdleTicks += now - idleStartTick;
                    idleStartTick = stopwatch.ElapsedTicks;
                }
            }
        }
        public double GetTotalIdleTime()
        {
            return totalIdleTicks * 1000.0 / Stopwatch.Frequency;
        }
        public double GetTotalTime()
        {
            return stopwatch.ElapsedMilliseconds;
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
    public class procEventArgs : EventArgs
    {
        public int id { get; set; }
    }
}