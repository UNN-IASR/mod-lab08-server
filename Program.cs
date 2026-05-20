#pragma warning disable CA1416, CS8632
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lab08
{
    public class ClientRequestEventArgs : EventArgs
    {
        public int ClientId { get; set; }
        public DateTime RequestTime { get; set; }
        public int RequestId { get; set; }
    }

    public class Client
    {
        public int ClientId { get; private set; }
        public event EventHandler<ClientRequestEventArgs>? OnRequest;

        public Client(Server server) => OnRequest += server.HandleClientRequest;

        public void SendRequest(int requestId) => OnRequest?.Invoke(this, new ClientRequestEventArgs 
        { 
            ClientId = this.ClientId, 
            RequestTime = DateTime.Now, 
            RequestId = requestId 
        });
    }

    public class Channel
    {
        public bool IsBusy { get; set; }
        private double _serviceIntensity;
        private static readonly Random _random = new Random();
        private static readonly object _lock = new object();

        public Channel(int id, double serviceIntensity) => _serviceIntensity = serviceIntensity;

        public async Task ProcessRequest()
        {
            IsBusy = true;
            double u; lock (_lock) { u = _random.NextDouble(); }
            await Task.Delay((int)(-Math.Log(1 - u) / _serviceIntensity * 1000));
            IsBusy = false;
        }
    }

    public class Server
    {
        private List<Channel> _channels;
        private int _total = 0, _served = 0, _rejected = 0;
        private List<int> _history = new();

        public Server(int channelsCount, double serviceIntensity) => 
            _channels = Enumerable.Range(0, channelsCount).Select(i => new Channel(i, serviceIntensity)).ToList();

        public async void HandleClientRequest(object? sender, ClientRequestEventArgs e)
        {
            Interlocked.Increment(ref _total);
            lock (_history) _history.Add(_channels.Count(c => c.IsBusy));
            
            var free = _channels.FirstOrDefault(c => !c.IsBusy);
            if (free != null) { Interlocked.Increment(ref _served); await free.ProcessRequest(); }
            else Interlocked.Increment(ref _rejected);
        }

        public (int total, int served, int rejected) GetStats() => (_total, _served, _rejected);
        public void Reset() { _total = _served = _rejected = 0; lock (_history) _history.Clear(); }

        public (double idle, double reject, double relQ, double absA, double avgK) CalcMetrics(double simTime)
        {
            double total = _total, served = _served, rejected = _rejected;
            return (
                _history.Count > 0 ? _history.Count(b => b == 0) / (double)_history.Count : 1,
                total > 0 ? rejected / total : 0,
                total > 0 ? served / total : 0,
                simTime > 0 ? served / simTime : 0,
                _history.Count > 0 ? _history.Average() : 0
            );
        }
    }

    public static class Theory
    {
        private static double Fact(int n) => n <= 1 ? 1 : Enumerable.Range(2, n - 1).Aggregate(1.0, (a, b) => a * b);

        public static double P0(double lambda, double mu, int n)
        {
            double rho = lambda / mu;
            return 1 / Enumerable.Range(0, n + 1).Sum(k => Math.Pow(rho, k) / Fact(k));
        }

        public static double Prej(double lambda, double mu, int n) => P0(lambda, mu, n) * Math.Pow(lambda / mu, n) / Fact(n);
        public static double Q(double lambda, double mu, int n) => 1 - Prej(lambda, mu, n);
        public static double A(double lambda, double mu, int n) => lambda * Q(lambda, mu, n);
        public static double K(double lambda, double mu, int n) => lambda / mu * Q(lambda, mu, n);
    }

    public static class Charts
    {
        public static void Draw(string title, string xLabel, string yLabel, double[] x, double[] yExp, double[] yTheor, string file)
        {
            const int Width = 800, Height = 600, Left = 80, Top = 60, 
            RightWidth = Width - Left - 40, RightHeight = Height - Top - 60;
            using var bmp = new Bitmap(Width, Height);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            g.DrawRectangle(Pens.Black, Left, Top, RightWidth, RightHeight);

            using var titleFont = new Font("Arial", 14, FontStyle.Bold);
            var titleSize = g.MeasureString(title, titleFont);
            g.DrawString(title, titleFont, Brushes.Black, (Width - titleSize.Width) / 2, 15);
            g.DrawString(xLabel, new Font("Arial", 11, FontStyle.Bold), Brushes.Black, Width / 2 - 50, Height - 25);

            using var sf = new StringFormat { FormatFlags = StringFormatFlags.DirectionVertical };
            g.DrawString(yLabel, new Font("Arial", 11, FontStyle.Bold), Brushes.Black, 20, Height / 2, sf);

            double minX = x.Min(), maxX = x.Max(), minY = Math.Min(yExp.Min(), yTheor.Min()), maxY = Math.Max(yExp.Max(), yTheor.Max());
            if (minY > 0) minY = 0;
            minY = Math.Floor(minY * 10) / 10;
            maxY = Math.Ceiling(maxY * 10) / 10;

            for (int i = 0; i <= 6; i++)
            {
                double xVal = minX + (maxX - minX) * i / 6;
                int xPos = Left + (int)(RightWidth * (xVal - minX) / (maxX - minX));
                g.DrawLine(Pens.LightGray, xPos, Top, xPos, Top + RightHeight);
                g.DrawString(xVal.ToString("F1"), new Font("Arial", 9), Brushes.Black, xPos - 15, Top + RightHeight + 5);
            }

            for (int i = 0; i <= 5; i++)
            {
                double yVal = minY + (maxY - minY) * i / 5;
                int yPos = Top + RightHeight - (int)(RightHeight * (yVal - minY) / (maxY - minY));
                g.DrawLine(Pens.LightGray, Left, yPos, Left + RightWidth, yPos);
                g.DrawString(yVal.ToString("F2"), new Font("Arial", 9), Brushes.Black, 5, yPos - 8);
            }

            var expPts = new PointF[x.Length];
            var theorPts = new PointF[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                int xPos = Left + (int)(RightWidth * (x[i] - minX) / (maxX - minX));
                int yExpPos = Top + RightHeight - (int)(RightHeight * (yExp[i] - minY) / (maxY - minY));
                int yTheorPos = Top + RightHeight - (int)(RightHeight * (yTheor[i] - minY) / (maxY - minY));
                expPts[i] = new PointF(xPos, yExpPos);
                theorPts[i] = new PointF(xPos, yTheorPos);
                g.FillEllipse(Brushes.Blue, xPos - 4, yExpPos - 4, 8, 8);
                g.FillEllipse(Brushes.Red, xPos - 4, yTheorPos - 4, 8, 8);
            }
            g.DrawLines(new Pen(Color.Blue, 2), expPts);
            using var dashPen = new Pen(Color.Red, 2) { DashStyle = DashStyle.Dash };
            g.DrawLines(dashPen, theorPts);

            g.FillRectangle(Brushes.White, Width - 140, Top, 130, 50);
            g.DrawRectangle(Pens.Black, Width - 140, Top, 130, 50);
            g.DrawLine(new Pen(Color.Blue, 2), Width - 130, Top + 15, Width - 100, Top + 15);
            g.DrawString("Эксперимент", new Font("Arial", 10), Brushes.Black, Width - 95, Top + 10);
            g.DrawLine(dashPen, Width - 130, Top + 35, Width - 100, Top + 35);
            g.DrawString("Теория", new Font("Arial", 10), Brushes.Black, Width - 95, Top + 30);

            Directory.CreateDirectory("result");
            bmp.Save(file, ImageFormat.Png);
        }
    }

    class Program
    {
        static async Task Main()
        {
            const int n = 3;
            const double mu = 10.0;
            double lambdaStart = 2.0, lambdaEnd = 25.0;
            int points = 12;
            double simTime = 30.0;

            Console.WriteLine($"n={n}, μ={mu}, λ∈[{lambdaStart};{lambdaEnd}], точек={points}, время={simTime}с\n");

            var lambdas = new List<double>();
            var expResults = new List<(double idle, double reject, double relQ, double absA, double avgK)>();
            var theorResults = new List<(double idle, double reject, double relQ, double absA, double avgK)>();

            double step = (lambdaEnd - lambdaStart) / (points - 1);

            for (int i = 0; i < points; i++)
            {
                double lambda = lambdaStart + i * step;
                lambdas.Add(lambda);
                Console.Write($"[{i + 1}/{points}] λ={lambda:F2} запросов/сек: ");

                var server = new Server(n, mu);
                await Simulate(server, lambda, simTime);
                expResults.Add(server.CalcMetrics(simTime));
                theorResults.Add((Theory.P0(lambda, mu, n), Theory.Prej(lambda, mu, n), 
                                  Theory.Q(lambda, mu, n), Theory.A(lambda, mu, n), 
                                  Theory.K(lambda, mu, n)));

                var stats = server.GetStats();
                Console.WriteLine($"поступило={stats.total,5} обслужено={stats.served,5} отказано={stats.rejected,4} | " +
                    $"Вероят.отк_exp={expResults.Last().reject:F4} theor={theorResults.Last().reject:F4}");
            }

            SaveResults(lambdas, expResults, theorResults);
            DrawAllGraphs(lambdas.ToArray(), expResults, theorResults);

            Console.WriteLine("\nРезультаты в result/");
        }

        static async Task Simulate(Server server, double lambda, double simTime)
        {
            server.Reset();
            var rand = new Random();
            var clients = Enumerable.Range(0, 20).Select(_ => new Client(server)).ToList();
            var start = DateTime.Now;
            int reqCount = 0;

            while ((DateTime.Now - start).TotalSeconds < simTime && reqCount < 200000)
            {
                await Task.Delay(Math.Max(1, (int)(-Math.Log(1 - rand.NextDouble()) / lambda * 1000)));
                clients[rand.Next(clients.Count)].SendRequest(++reqCount);
            }
            await Task.Delay(1000);
        }

        static void SaveResults(List<double> lambdas, 
            List<(double idle, double reject, double relQ, double absA, double avgK)> exp,
            List<(double idle, double reject, double relQ, double absA, double avgK)> theor)
        {
            Directory.CreateDirectory("result");
            using var w = new StreamWriter("result/results.txt", false, Encoding.UTF8);
            w.WriteLine("λ\tP0_exp\tВероят.отк_exp\tQ_exp\tA_exp\tk_exp\tP0_теор\tВероят.отк_теор\tQ_теор\tA_теор\tk_теор\tПогрешн(%)");
            for (int i = 0; i < lambdas.Count; i++)
            {
                double err = theor[i].reject > 0 ? Math.Abs(exp[i].reject - theor[i].reject) / theor[i].reject * 100 : 0;
                w.WriteLine($"{lambdas[i]:F2}\t{exp[i].idle:F4}\t{exp[i].reject:F4}\t{exp[i].relQ:F4}\t{exp[i].absA:F4}\t{exp[i].avgK:F4}\t" +
                    $"{theor[i].idle:F4}\t{theor[i].reject:F4}\t{theor[i].relQ:F4}\t{theor[i].absA:F4}\t{theor[i].avgK:F4}\t{err:F2}");
            }
        }

        static void DrawAllGraphs(double[] x, 
            List<(double idle, double reject, double relQ, double absA, double avgK)> exp,
            List<(double idle, double reject, double relQ, double absA, double avgK)> theor)
        {
            var e = exp.ToArray();
            var t = theor.ToArray();
            Charts.Draw("Вероятность простоя P₀", "λ (запросов/сек)", "P₀", x, e.Select(v => v.idle).ToArray(), t.Select(v => v.idle).ToArray(), "result/p-1.png");
            Charts.Draw("Вероятность отказа", "λ (запросов/сек)", "Вероят.отк", x, e.Select(v => v.reject).ToArray(), t.Select(v => v.reject).ToArray(), "result/p-2.png");
            Charts.Draw("Относительная пропускная способность Q", "λ (запросов/сек)", "Q", x, e.Select(v => v.relQ).ToArray(), t.Select(v => v.relQ).ToArray(), "result/p-3.png");
            Charts.Draw("Абсолютная пропускная способность A", "λ (запросов/сек)", "A", x, e.Select(v => v.absA).ToArray(), t.Select(v => v.absA).ToArray(), "result/p-4.png");
            Charts.Draw("Среднее число занятых каналов k", "λ (запросов/сек)", "k", x, e.Select(v => v.avgK).ToArray(), t.Select(v => v.avgK).ToArray(), "result/p-5.png");
        }
    }
}
