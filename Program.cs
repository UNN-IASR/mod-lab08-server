using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lab08
{
    // ======================== Класс Client ========================
    public class Client
    {
        private static readonly Random Random = new Random();
        private readonly Server _server;
        private readonly int _clientId;
        private Thread _clientThread;

        public event EventHandler<RequestEventArgs> RequestGenerated;

        public Client(int id, Server server)
        {
            _clientId = id;
            _server = server;
        }

        public void Start(double intensity)
        {
            _clientThread = new Thread(() => GenerateRequests(intensity));
            _clientThread.IsBackground = true;
            _clientThread.Start();
        }

        private void GenerateRequests(double intensity)
        {
            while (true)
            {
                double interval = -Math.Log(1.0 - Random.NextDouble()) / intensity;
                Thread.Sleep(TimeSpan.FromSeconds(interval));
                OnRequestGenerated(new RequestEventArgs(_clientId, DateTime.Now));
            }
        }

        protected virtual void OnRequestGenerated(RequestEventArgs e) =>
            RequestGenerated?.Invoke(this, e);
    }

    public class RequestEventArgs : EventArgs
    {
        public int ClientId { get; }
        public DateTime RequestTime { get; }
        public RequestEventArgs(int clientId, DateTime requestTime)
        {
            ClientId = clientId;
            RequestTime = requestTime;
        }
    }

    // ======================== Класс Server ========================
    public class Server
    {
        private readonly int _channelsCount;
        private readonly double _serviceIntensity;
        private readonly SemaphoreSlim _semaphore;
        private readonly ServiceChannel[] _channels;

        private int _totalRequests;
        private int _servedRequests;
        private int _rejectedRequests;
        private double _totalBusyChannels;
        private int _measurementCount;
        private readonly object _statsLock = new object();

        public Server(int channelsCount, double serviceIntensity)
        {
            _channelsCount = channelsCount;
            _serviceIntensity = serviceIntensity;
            _semaphore = new SemaphoreSlim(channelsCount, channelsCount);
            _channels = new ServiceChannel[channelsCount];
            for (int i = 0; i < channelsCount; i++)
                _channels[i] = new ServiceChannel(i, serviceIntensity);
        }

        public void HandleRequest(object sender, RequestEventArgs e)
        {
            Interlocked.Increment(ref _totalRequests);
            lock (_statsLock)
            {
                _totalBusyChannels += GetBusyChannelsCount();
                _measurementCount++;
            }

            if (_semaphore.Wait(0))
            {
                // Нашли свободный канал
                ServiceChannel freeChannel = null;
                lock (_channels)
                {
                    freeChannel = _channels.FirstOrDefault(ch => !ch.IsBusy);
                }
                if (freeChannel != null)
                {
                    Interlocked.Increment(ref _servedRequests);
                    Task.Run(() => freeChannel.ProcessRequest(e.ClientId, e.RequestTime));
                }
                else
                {
                    _semaphore.Release();
                    Interlocked.Increment(ref _rejectedRequests);
                }
            }
            else
            {
                Interlocked.Increment(ref _rejectedRequests);
            }
        }

        private int GetBusyChannelsCount()
        {
            lock (_channels)
                return _channels.Count(ch => ch.IsBusy);
        }

        public void ResetStats()
        {
            lock (_statsLock)
            {
                _totalRequests = 0;
                _servedRequests = 0;
                _rejectedRequests = 0;
                _totalBusyChannels = 0;
                _measurementCount = 0;
            }
            foreach (var ch in _channels) ch.Reset();
        }

        public Statistics GetStatistics()
        {
            lock (_statsLock)
            {
                double avgBusy = _measurementCount > 0 ? _totalBusyChannels / _measurementCount : 0;
                return new Statistics
                {
                    TotalRequests = _totalRequests,
                    ServedRequests = _servedRequests,
                    RejectedRequests = _rejectedRequests,
                    AverageBusyChannels = avgBusy
                };
            }
        }
    }

    public class ServiceChannel
    {
        private static readonly Random Random = new Random();
        private readonly int _id;
        private readonly double _serviceIntensity;
        private bool _isBusy;
        private readonly object _lock = new object();

        public bool IsBusy { get { lock (_lock) return _isBusy; } }

        public ServiceChannel(int id, double serviceIntensity)
        {
            _id = id;
            _serviceIntensity = serviceIntensity;
            _isBusy = false;
        }

        public void ProcessRequest(int clientId, DateTime requestTime)
        {
            lock (_lock) _isBusy = true;
            double serviceTime = -Math.Log(1.0 - Random.NextDouble()) / _serviceIntensity;
            Thread.Sleep(TimeSpan.FromSeconds(serviceTime));
            lock (_lock) _isBusy = false;
        }

        public void Reset() { lock (_lock) _isBusy = false; }
    }

    public class Statistics
    {
        public int TotalRequests { get; set; }
        public int ServedRequests { get; set; }
        public int RejectedRequests { get; set; }
        public double AverageBusyChannels { get; set; }
        public double FailureProbability => TotalRequests > 0 ? (double)RejectedRequests / TotalRequests : 0;
        public double RelativeThroughput => TotalRequests > 0 ? (double)ServedRequests / TotalRequests : 0;
        public double AbsoluteThroughput => ServedRequests / 3600.0; // заявок в секунду (усреднённо)
    }

    // ======================== Класс Program ========================
    class Program
    {
        private const int SimulationSeconds = 300;      // длительность одного эксперимента (сек)
        private const int Channels = 3;                 // число каналов n
        private const double Mu = 1.0;                  // интенсивность обслуживания μ (заявок/сек)

        static void Main()
        {
            Console.WriteLine("=== Моделирование СМО с отказами (M/M/3/0) ===");
            Console.WriteLine($"Каналов: {Channels}, μ = {Mu} заявок/сек, время эксперимента: {SimulationSeconds} сек\n");

            // 10 значений λ от 0.3 до 3.0 с шагом 0.3
            double[] lambda = Enumerable.Range(1, 10).Select(i => i * 0.3).ToArray();
            var expResults = new List<SimulationResult>();
            var theorResults = new List<TheoreticalResult>();

            for (int idx = 0; idx < lambda.Length; idx++)
            {
                double lam = lambda[idx];
                Console.Write($"λ = {lam:F2} → эксперимент... ");
                var sim = RunSimulation(lam);
                expResults.Add(sim);
                var theor = CalculateTheoretical(lam);
                theorResults.Add(theor);
                Console.WriteLine($"Pотк = {sim.FailureProbability:F4} (теор: {theor.FailureProbability:F4})");
            }

            // Вывод таблицы
            Console.WriteLine("\n" + new string('=', 100));
            Console.WriteLine("РЕЗУЛЬТАТЫ СРАВНЕНИЯ");
            Console.WriteLine("λ\tPотк(экс)\tPотк(теор)\tQ(экс)\tQ(теор)\tA(экс)\tA(теор)\tk(экс)\tk(теор)\tP0(теор)");
            Console.WriteLine(new string('-', 100));
            for (int i = 0; i < lambda.Length; i++)
            {
                var e = expResults[i];
                var t = theorResults[i];
                Console.WriteLine($"{lambda[i]:F2}\t{e.FailureProbability:F4}\t\t{t.FailureProbability:F4}\t\t" +
                                  $"{e.RelativeThroughput:F4}\t{t.RelativeThroughput:F4}\t" +
                                  $"{e.AbsoluteThroughput:F4}\t{t.AbsoluteThroughput:F4}\t" +
                                  $"{e.AverageBusyChannels:F4}\t{t.AverageBusyChannels:F4}\t{t.IdleProbability:F4}");
            }

            SaveResults(expResults, theorResults, lambda);
            GeneratePlots(expResults, theorResults, lambda);
            Console.WriteLine("\nРабота завершена. Результаты сохранены в results.txt и графиках result/*.png");
        }

        static SimulationResult RunSimulation(double lambda)
        {
            var server = new Server(Channels, Mu);
            const int clientCount = 5;
            double perClientIntensity = lambda / clientCount;
            var clients = new List<Client>();

            for (int i = 0; i < clientCount; i++)
            {
                var c = new Client(i, server);
                c.RequestGenerated += server.HandleRequest;
                clients.Add(c);
                c.Start(perClientIntensity);
            }

            Thread.Sleep(TimeSpan.FromSeconds(SimulationSeconds));
            var stat = server.GetStatistics();

            // Теоретическая P0 для расчёта в экспериментальных данных (только для отображения)
            double rho = lambda / Mu;
            double sum = 0;
            for (int i = 0; i <= Channels; i++) sum += Math.Pow(rho, i) / Factorial(i);
            double p0_theor = 1.0 / sum;

            return new SimulationResult
            {
                Lambda = lambda,
                FailureProbability = stat.FailureProbability,
                RelativeThroughput = stat.RelativeThroughput,
                AbsoluteThroughput = stat.AbsoluteThroughput,
                AverageBusyChannels = stat.AverageBusyChannels,
                IdleProbability = p0_theor
            };
        }

        static TheoreticalResult CalculateTheoretical(double lambda)
        {
            double rho = lambda / Mu;
            int n = Channels;
            double sum = 0;
            for (int i = 0; i <= n; i++) sum += Math.Pow(rho, i) / Factorial(i);
            double p0 = 1.0 / sum;
            double pFail = (Math.Pow(rho, n) / Factorial(n)) * p0;
            double q = 1 - pFail;
            double a = lambda * q;
            double k = rho * q;
            return new TheoreticalResult
            {
                Lambda = lambda,
                FailureProbability = pFail,
                RelativeThroughput = q,
                AbsoluteThroughput = a,
                AverageBusyChannels = k,
                IdleProbability = p0
            };
        }

        static int Factorial(int n)
        {
            int res = 1;
            for (int i = 2; i <= n; i++) res *= i;
            return res;
        }

        static void SaveResults(List<SimulationResult> exp, List<TheoreticalResult> theor, double[] lambda)
        {
            using var sw = new StreamWriter("results.txt", false, System.Text.Encoding.UTF8);
            sw.WriteLine("Лабораторная работа №8. Моделирование СМО с отказами");
            sw.WriteLine($"Каналов = {Channels}, μ = {Mu} заявок/сек, время эксперимента = {SimulationSeconds} сек");
            sw.WriteLine(new string('=', 90));
            sw.WriteLine("λ\tPотк(экс)\tPотк(теор)\tQ(экс)\tQ(теор)\tA(экс)\tA(теор)\tk(экс)\tk(теор)\tP0(теор)");
            for (int i = 0; i < lambda.Length; i++)
            {
                sw.WriteLine($"{lambda[i]:F2}\t{exp[i].FailureProbability:F4}\t\t{theor[i].FailureProbability:F4}\t\t" +
                             $"{exp[i].RelativeThroughput:F4}\t{theor[i].RelativeThroughput:F4}\t" +
                             $"{exp[i].AbsoluteThroughput:F4}\t{theor[i].AbsoluteThroughput:F4}\t" +
                             $"{exp[i].AverageBusyChannels:F4}\t{theor[i].AverageBusyChannels:F4}\t{theor[i].IdleProbability:F4}");
            }
        }

        static void GeneratePlots(List<SimulationResult> exp, List<TheoreticalResult> theor, double[] lambda)
        {
            Directory.CreateDirectory("result");

            double[] x = lambda;
            double[] yExpPfail = exp.Select(r => r.FailureProbability).ToArray();
            double[] yTheorPfail = theor.Select(r => r.FailureProbability).ToArray();
            CreatePlot(x, yExpPfail, yTheorPfail, "Вероятность отказа", "result/p-1.png");

            double[] yExpQ = exp.Select(r => r.RelativeThroughput).ToArray();
            double[] yTheorQ = theor.Select(r => r.RelativeThroughput).ToArray();
            CreatePlot(x, yExpQ, yTheorQ, "Относительная пропускная способность", "result/p-2.png");

            double[] yExpA = exp.Select(r => r.AbsoluteThroughput).ToArray();
            double[] yTheorA = theor.Select(r => r.AbsoluteThroughput).ToArray();
            CreatePlot(x, yExpA, yTheorA, "Абсолютная пропускная способность", "result/p-3.png");

            double[] yExpK = exp.Select(r => r.AverageBusyChannels).ToArray();
            double[] yTheorK = theor.Select(r => r.AverageBusyChannels).ToArray();
            CreatePlot(x, yExpK, yTheorK, "Среднее число занятых каналов", "result/p-4.png");

            double[] yExpP0 = exp.Select(r => r.IdleProbability).ToArray();
            double[] yTheorP0 = theor.Select(r => r.IdleProbability).ToArray();
            CreatePlot(x, yExpP0, yTheorP0, "Вероятность простоя системы", "result/p-5.png");
        }

        static void CreatePlot(double[] x, double[] yExp, double[] yTheor, string title, string filename)
        {
            var plt = new ScottPlot.Plot();
            plt.Title(title);
            plt.XLabel("Интенсивность входного потока λ (заявок/сек)");
            plt.YLabel(title);

            // Экспериментальные данные (красные точки с линией)
            var scatterExp = plt.Add.Scatter(x, yExp);
            scatterExp.Label = "Эксперимент";
            scatterExp.Color = new ScottPlot.Color(255, 0, 0); // красный
            scatterExp.MarkerSize = 5;
            scatterExp.LineWidth = 1;

            // Теоретические данные (синие точки с линией)
            var scatterTheor = plt.Add.Scatter(x, yTheor);
            scatterTheor.Label = "Теория";
            scatterTheor.Color = new ScottPlot.Color(0, 0, 255); // синий
            scatterTheor.MarkerSize = 5;
            scatterTheor.LineWidth = 1;

            plt.ShowLegend();
            plt.SavePng(filename, 800, 600);
        }
        // Вспомогательные классы для хранения результатов
        public class SimulationResult
    {
        public double Lambda { get; set; }
        public double FailureProbability { get; set; }
        public double RelativeThroughput { get; set; }
        public double AbsoluteThroughput { get; set; }
        public double AverageBusyChannels { get; set; }
        public double IdleProbability { get; set; }
    }

    public class TheoreticalResult
    {
        public double Lambda { get; set; }
        public double FailureProbability { get; set; }
        public double RelativeThroughput { get; set; }
        public double AbsoluteThroughput { get; set; }
        public double AverageBusyChannels { get; set; }
        public double IdleProbability { get; set; }
    }
}
}