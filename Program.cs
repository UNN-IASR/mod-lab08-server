using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ScottPlot;

namespace Lab08
{
    public class Server
    {
        private int totalChannels;
        private bool[] channels;
        private object lockObj = new object();

        public int ProcessedCount { get; private set; }
        public int RejectedCount { get; private set; }
        public int TotalRequests { get; private set; }

        private double mu;

        public Server(int n, double mu)
        {
            this.totalChannels = n;
            this.mu = mu;
            this.channels = new bool[n];
            ProcessedCount = 0;
            RejectedCount = 0;
            TotalRequests = 0;
        }

        public void HandleRequest(object? sender, EventArgs e)
        {
            int freeChannel = -1;

            lock (lockObj)
            {
                TotalRequests++;
                for (int i = 0; i < totalChannels; i++)
                {
                    if (!channels[i])
                    {
                        freeChannel = i;
                        channels[i] = true;
                        break;
                    }
                }
            }

            if (freeChannel != -1)
            {
                Task.Run(() => ProcessWork(freeChannel));
            }
            else
            {
                lock (lockObj) { RejectedCount++; }
            }
        }

        private void ProcessWork(int channelIndex)
        {
            int processingTime = (int)((1.0 / mu) * 1000);
            Thread.Sleep(processingTime);

            lock (lockObj)
            {
                ProcessedCount++;
                channels[channelIndex] = false;
            }
        }
    }

    public class Client
    {
        public event EventHandler? OnRequest;

        public Client(Server server)
        {
            this.OnRequest += server.HandleRequest;
        }

        public void StartGenerating(int requestCount, double lambda)
        {
            Random rand = new Random();
            for (int i = 0; i < requestCount; i++)
            {
                double r = rand.NextDouble();
                if (r == 0) r = 0.00001;

                double delay = -Math.Log(r) / lambda;
                int sleepTime = (int)(delay * 1000);

                if (sleepTime > 0)
                    Thread.Sleep(sleepTime);

                OnRequest?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    class Program
    {
        static double Factorial(int n)
        {
            if (n <= 1) return 1;
            return n * Factorial(n - 1);
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Запуск моделирования СМО... Это займет пару минут.");

            int n = 3;
            double mu = 20.0;
            int requestCount = 500;
            int points = 10;

            double[] lambdas = new double[points];

            double[] p0_Theory = new double[points];
            double[] pRej_Theory = new double[points];
            double[] q_Theory = new double[points];
            double[] a_Theory = new double[points];
            double[] k_Theory = new double[points];

            double[] p0_Practice = new double[points];
            double[] pRej_Practice = new double[points];
            double[] q_Practice = new double[points];
            double[] a_Practice = new double[points];
            double[] k_Practice = new double[points];

            StringBuilder report = new StringBuilder();
            report.AppendLine("Отчет по результатам моделирования СМО 'клиент-сервер'\n");
            report.AppendLine($"Параметры: Каналов (n) = {n}, Интенсивность обслуживания (mu) = {mu}, Заявок = {requestCount}\n");

            for (int i = 0; i < points; i++)
            {
                double lambda = 10 + (i * 10);
                lambdas[i] = lambda;

                Console.WriteLine($"Симуляция {i + 1}/{points}: λ = {lambda}");

                double ro = lambda / mu;
                double sum = 0;
                for (int j = 0; j <= n; j++)
                {
                    sum += Math.Pow(ro, j) / Factorial(j);
                }

                p0_Theory[i] = 1.0 / sum;
                pRej_Theory[i] = (Math.Pow(ro, n) / Factorial(n)) * p0_Theory[i];
                q_Theory[i] = 1.0 - pRej_Theory[i];
                a_Theory[i] = lambda * q_Theory[i];
                k_Theory[i] = ro * q_Theory[i];

                Server server = new Server(n, mu);
                Client client = new Client(server);

                client.StartGenerating(requestCount, lambda);

                Thread.Sleep(1000);

                int total = server.TotalRequests;
                int rejected = server.RejectedCount;
                int processed = server.ProcessedCount;

                pRej_Practice[i] = (double)rejected / total;
                q_Practice[i] = (double)processed / total;
                a_Practice[i] = lambda * q_Practice[i];
                k_Practice[i] = a_Practice[i] / mu;

                p0_Practice[i] = pRej_Practice[i] * Factorial(n) / Math.Pow(ro, n);

                report.AppendLine($"--- Эксперимент с λ = {lambda} ---");
                report.AppendLine($"Поступило: {total}, Обслужено: {processed}, Отклонено: {rejected}");
                report.AppendLine($"[Теория] Pотк: {pRej_Theory[i]:F4}, Q: {q_Theory[i]:F4}, A: {a_Theory[i]:F4}, K: {k_Theory[i]:F4}");
                report.AppendLine($"[Опыт]   Pотк: {pRej_Practice[i]:F4}, Q: {q_Practice[i]:F4}, A: {a_Practice[i]:F4}, K: {k_Practice[i]:F4}\n");
            }

            File.WriteAllText("results.txt", report.ToString(), Encoding.UTF8);
            Console.WriteLine("\nОтчет results.txt сохранен.");

            Directory.CreateDirectory("result");

            SavePlot("result/p-1.png", "Вероятность простоя (P0)", lambdas, p0_Theory, p0_Practice);
            SavePlot("result/p-2.png", "Вероятность отказа (Pотк)", lambdas, pRej_Theory, pRej_Practice);
            SavePlot("result/p-3.png", "Относительная пропускная способность (Q)", lambdas, q_Theory, q_Practice);
            SavePlot("result/p-4.png", "Абсолютная пропускная способность (A)", lambdas, a_Theory, a_Practice);
            SavePlot("result/p-5.png", "Среднее число занятых каналов (k)", lambdas, k_Theory, k_Practice);

            Console.WriteLine("Графики сохранены в папку result. Работа завершена!");
        }

        static void SavePlot(string filename, string title, double[] xs, double[] yTheory, double[] yPractice)
        {
            ScottPlot.Plot myPlot = new();

            var scatter1 = myPlot.Add.Scatter(xs, yTheory);
            scatter1.LegendText = "Теория";

            var scatter2 = myPlot.Add.Scatter(xs, yPractice);
            scatter2.LegendText = "Практика";

            myPlot.Title(title);
            myPlot.XLabel("Интенсивность вх. потока (λ)");
            myPlot.YLabel("Значение");
            myPlot.ShowLegend();

            myPlot.SavePng(filename, 800, 600);
        }
    }
}