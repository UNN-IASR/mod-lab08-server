using System;
using System.IO;
using System.Threading;
using ScottPlot;

namespace TPProj
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("=== Запуск исследования СМО ===");
            
            double[] x_lambda = new double[10];
            
            double[] y_p0_th = new double[10];
            double[] y_potk_th = new double[10];
            double[] y_q_th = new double[10];
            double[] y_a_th = new double[10];
            double[] y_k_th = new double[10];

            double[] y_p0_pr = new double[10];
            double[] y_potk_pr = new double[10];
            double[] y_q_pr = new double[10];
            double[] y_a_pr = new double[10];
            double[] y_k_pr = new double[10];

            int n = 5;
            double mu = 2.0;
            int testRequestsCount = 50;

            string report = "lambda\tP0_exp\tPn_exp\tQ_exp\tA_exp\tk_exp\tP0_th\tPn_th\tQ_th\tA_th\tk_th\n";

            for (int step = 1; step <= 10; step++)
            {
                double lambda = step * 2.0; 
                x_lambda[step - 1] = lambda;
                
                Server testServer = new Server();
                Client testClient = new Client(testServer);

                for (int id = 1; id <= testRequestsCount; id++)
                {
                    testClient.send(id);
                    double interval = -Math.Log(Random.Shared.NextDouble()) / lambda;
                    Thread.Sleep((int)(interval * 1000)); 
                }

                Thread.Sleep(1000); 

                double rho = lambda / mu; 
                double sum = 0;
                for (int k = 0; k <= n; k++)
                {
                    sum += Math.Pow(rho, k) / Factorial(k); 
                }
                double p0_th = 1.0 / sum; 
                double potk_th = (Math.Pow(rho, n) / Factorial(n)) * p0_th; 
                double q_th = 1.0 - potk_th; 
                double a_th = lambda * q_th; 
                double k_th = rho * q_th; 

                double potk_pr = (double)testServer.rejectedCount / testServer.requestCount;
                double q_pr = (double)testServer.processedCount / testServer.requestCount;
                double a_pr = lambda * q_pr;
                double k_pr = rho * q_pr;

                double rho_pr = lambda / mu; 
                double sum_pr = 0;
                for (int k = 0; k <= n; k++)
                {
                    sum_pr += Math.Pow(rho_pr, k) / Factorial(k);
                }
                double p0_pr = 1.0 / sum_pr; 

                y_p0_th[step - 1] = p0_th;
                y_potk_th[step - 1] = potk_th;
                y_q_th[step - 1] = q_th;
                y_a_th[step - 1] = a_th;
                y_k_th[step - 1] = k_th;

                y_p0_pr[step - 1] = p0_pr;
                y_potk_pr[step - 1] = potk_pr;
                y_q_pr[step - 1] = q_pr;
                y_a_pr[step - 1] = a_pr;
                y_k_pr[step - 1] = k_pr;
                
                report += string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F2}\t{1:F3}\t{2:F3}\t{3:F3}\t{4:F3}\t{5:F3}\t{6:F3}\t{7:F3}\t{8:F3}\t{9:F3}\t{10:F3}\n",
                    lambda, p0_pr, potk_pr, q_pr, a_pr, k_pr, p0_th, potk_th, q_th, a_th, k_th);

                Console.WriteLine($"Точка {step}/10 (Lambda = {lambda:F1}) успешно просчитана...");
            }

            string currentDir = Directory.GetCurrentDirectory();
            string? parentDir = Directory.GetParent(currentDir)?.FullName ?? currentDir;
            string outputDir = Path.Combine(parentDir, "result");

            if (!Directory.Exists(outputDir)) 
            {
                Directory.CreateDirectory(outputDir);
            }

            string reportFilePath = Path.Combine(parentDir, "result.txt");
            File.WriteAllText(reportFilePath, report);
            Console.WriteLine($"\nОтчет записан в: {reportFilePath}");

            SavePlot(x_lambda, y_p0_th, y_p0_pr, "Вероятность простоя системы (P0)", Path.Combine(outputDir, "p-1.png"));
            SavePlot(x_lambda, y_potk_th, y_potk_pr, "Вероятность отказа системы (Pотк)", Path.Combine(outputDir, "p-2.png"));
            SavePlot(x_lambda, y_q_th, y_q_pr, "Относительная пропускная способность (Q)", Path.Combine(outputDir, "p-3.png"));
            SavePlot(x_lambda, y_a_th, y_a_pr, "Абсолютная пропускная способность (A)", Path.Combine(outputDir, "p-4.png"));
            SavePlot(x_lambda, y_k_th, y_k_pr, "Среднее число занятых каналов (k)", Path.Combine(outputDir, "p-5.png"));

            Console.Clear();
            Console.WriteLine($"Графики сохранены в папку: {outputDir}\nТаблица сохранена в файл: {reportFilePath}");
            Console.ReadLine();
        }

        private static void SavePlot(double[] x, double[] yTh, double[] yPr, string title, string filePath)
        {
            var plt = new ScottPlot.Plot();
            plt.Title(title);
            plt.XLabel("Интенсивность входного потока (Lambda)");
            plt.YLabel("Значение параметра");

            var scatterTh = plt.Add.Scatter(x, yTh);
            scatterTh.Label = "Теория";
            scatterTh.Color = ScottPlot.Color.FromHex("#1F77B4");
            scatterTh.LineWidth = 2;
            scatterTh.MarkerSize = 0;

            var scatterPr = plt.Add.Scatter(x, yPr);
            scatterPr.Label = "Эксперимент";
            scatterPr.Color = ScottPlot.Color.FromHex("#D62728");
            scatterPr.LineWidth = 1;
            scatterPr.LineStyle.Pattern = LinePattern.Dashed;
            scatterPr.MarkerSize = 6;

            plt.ShowLegend(ScottPlot.Alignment.UpperRight);
            plt.SavePng(filePath, 600, 400);
        }

        private static double Factorial(int n)
        {
            double res = 1;
            for (int i = 1; i <= n; i++) res *= i;
            return res;
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

        public Server()
        {
            pool = new PoolRecord[5];
        }

        public void proc(object? sender, procEventArgs? e)
        {
            if (e == null) return;
            
            lock (threadLock)
            {
                Console.WriteLine("Заявка с номером: {0}", e.id);
                requestCount++;
                for (int i = 0; i < 5; i++)
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

        public void Answer(object? arg)
        {
            if (arg == null) return;
            int id = (int)arg;
            Console.WriteLine("Обработка заявки: {0}", id);
            Thread.Sleep(500);

            lock (threadLock)
            {
                for (int i = 0; i < 5; i++)
                    if (pool[i].thread == Thread.CurrentThread)
                        pool[i].in_use = false;
            }
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

        public event EventHandler<procEventArgs>? request;
    }

    public class procEventArgs : EventArgs
    {
        public int id { get; set; }
    }
}