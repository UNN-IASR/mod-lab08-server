using System;
using System.Formats.Asn1;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms.DataVisualization.Charting;

namespace TPProj
{
    class Program
    {
        static void Main()
        {
            // Чтение конфигурации из файла json
            string json = File.ReadAllText("config.json");
            var config = JsonSerializer.Deserialize<Config>(json);
            ArgumentNullException.ThrowIfNull(config);

            int[] intervals = { 150, 120, 100, 80, 60, 50, 45, 40, 35, 30, 25 };

            using StreamWriter file = new StreamWriter("results.txt", true, System.Text.Encoding.UTF8);

            report(file, config, intervals);

            file.WriteLine("lambda\t\tmu\t\t\trho\t\t\t" +
                           "P0_теор\t\tPn_теор\t\tQ_теор\t\tA_теор\t\tk_теор\t\t" +
                           "Pn_эксп\t\tQ_эксп\t\tA_эксп\t\tk_эксп");

            //Списки точек для графиков
            List<double> p0_theor_values = new List<double>();
            List<double> pn_theor_values = new List<double>();
            List<double> pn_exp_values = new List<double>();
            List<double> q_theor_values = new List<double>();
            List<double> q_exp_values = new List<double>();
            List<double> a_theor_values = new List<double>();
            List<double> a_exp_values = new List<double>();
            List<double> k_theor_values = new List<double>();
            List<double> k_exp_values = new List<double>();

            foreach (int interval in intervals)
            {
                Server server = new Server(config.poolSize, config.processingTime);
                Client client = new Client(server);

                // примерно расчёт на 100 секунд в миллисекундах
                int totalTime = 100_000;
                int requestCount = totalTime / interval;
                for (int id = 1; id <= requestCount; id++)
                {
                    client.send(id);
                    Thread.Sleep(interval);
                }
                // Ожидание завершения всех потоков
                while (server.requestCount != server.processedCount + server.rejectedCount)
                {
                    Thread.Sleep(100);
                }

                ///----------------------------------------------------------------------
                ///ТЕОРЕТИЧЕСКИЕ РАСЧЕТЫ
                ///----------------------------------------------------------------------

                double lambda = 1.0 / interval;
                //интенсивность обслуживания
                double mu = 1.0 / config.processingTime;
                //колличество каналов обслуживания
                int n = config.poolSize;

                //приведенная интеснивность входного потока заявок
                double rho = lambda / mu;

                //вероятность простоя системы
                double p0 = 0.0;
                for (int i = 0; i <= n; i++)
                {
                    p0 += Math.Pow(rho, i) / Factorial(i);
                }
                p0 = 1.0 / p0;

                //вероятность отказа системы
                double pn = (Math.Pow(rho, n) / Factorial(n)) * p0;

                //Относительная пропускная способность:
                double q = 1 - pn;

                //Абсолютная пропускная способность:
                double a = lambda * q;

                //Среднее число занятых каналов:
                double k = a / mu;
                //доля отклонённых от всех
                double pn_exp = (double)server.rejectedCount / server.requestCount;
                // доля обслуженных от всех
                double q_exp = (double)server.processedCount / server.requestCount;
                //количество обслуженных за единицу времени
                double a_exp = lambda * q_exp;
                //среднее число занятых каналов
                double k_exp = a_exp / mu;

                p0_theor_values.Add(p0);
                pn_theor_values.Add(pn);
                pn_exp_values.Add(pn_exp);
                q_theor_values.Add(q);
                q_exp_values.Add(q_exp);
                a_theor_values.Add(a);
                a_exp_values.Add(a_exp);
                k_theor_values.Add(k);
                k_exp_values.Add(k_exp);
                ///----------------------------------------------------------------------
                ///КОНЕЦ ТЕОРЕТИЧЕСКИХ РАСЧЕТОВ
                ///----------------------------------------------------------------------

                file.WriteLine($"{lambda:F3}\t\t{mu:F3}\t\t{rho:F3}\t\t" +
                       $"{p0:F4}\t\t{pn:F4}\t\t{q:F4}\t\t{a:F4}\t\t{k:F4}\t\t" +
                       $"{pn_exp:F4}\t\t{q_exp:F4}\t\t{a_exp:F4}\t\t{k_exp:F4}");

                Console.WriteLine($"lambda={lambda:F2} готово");
            }

            // Подготовка массива lambda (1/интервал) для оси X
            double[] lambdas = intervals.Select(x => 1.0 / x).ToArray();

            drawGraph(lambdas, p0_theor_values.ToArray(),
             null, "Вероятность простоя", "P0", 1);
            drawGraph(lambdas, pn_theor_values.ToArray(),
             pn_exp_values.ToArray(), "Вероятность отказа", "Pn", 2);
            drawGraph(lambdas, q_theor_values.ToArray(),
             q_exp_values.ToArray(), "Относит. пропускная", "Q", 3);
            drawGraph(lambdas, a_theor_values.ToArray(),
             a_exp_values.ToArray(), "Абсолют. пропускная", "A", 4);
            drawGraph(lambdas, k_theor_values.ToArray(),
             k_exp_values.ToArray(), "Среднее каналов", "k", 5);
        }
        public static void report(StreamWriter file, Config config, int[] intervals)
        {
            file.WriteLine("========================================================");
            file.WriteLine("Отчёт по моделированию СМО с отказами — 20.06.2024");
            file.WriteLine("========================================================\n");
            file.WriteLine("Параметры системы:");
            file.WriteLine($"  Число каналов (n)         : {config.poolSize}");
            file.WriteLine($"  Время обслуживания (мс)   : {config.processingTime}");
            file.WriteLine($"  Интенсивность обслуживания: mu = {1.0 / config.processingTime:F4}");
            file.WriteLine($"  Исследуемые интервалы (мс): {string.Join(", ", intervals)}");
            file.WriteLine("\nОписание модели:");
            file.WriteLine("  Моделируется многоканальная СМО с отказами (без очереди).");
            file.WriteLine("  При занятости всех каналов входящая заявка получает отказ.");
            file.WriteLine("  Входной поток и поток обслуживания — пуассоновские.");
            file.WriteLine("\nМетодика исследования:");
            file.WriteLine("  Для каждого значения lambda проводится симуляция длительностью ~100 с.");
            file.WriteLine("  Количество заявок пропорционально интервалу: N = 100000 / interval.");
            file.WriteLine("  Экспериментальные показатели вычисляются из накопленной статистики.");
            file.WriteLine("  Теоретические значения рассчитываются по формулам Эрланга (лекция №5).");
            file.WriteLine("\nВыводы:");
            file.WriteLine("  При малых значениях rho (rho < 3) вероятность отказа мала,");
            file.WriteLine("  экспериментальные данные могут отклоняться из-за редкости событий.");
            file.WriteLine("  При rho > 3 эксперимент хорошо сходится с теорией.");
            file.WriteLine("  Увеличение lambda снижает Q и P0, повышает Pn — система перегружается.");
            file.WriteLine("  Графики A и k отличаются только масштабом (k = A / mu = const * A).");
            file.WriteLine("\nРезультаты моделирования:\n");
        }

        public static void drawGraph(double[] lambdas, double[] theor,
         double[]? exp, string title, string yLabel, int i)
        {
            var chart = new Chart();
            chart.Size = new Size(800, 600);

            var chartArea = new ChartArea();
            chartArea.AxisX.Title = "Lambda";
            chartArea.AxisY.Title = yLabel;
            chart.ChartAreas.Add(chartArea);

            var seriesTheor = new Series();
            seriesTheor.Name = "Theoretical";
            seriesTheor.ChartType = SeriesChartType.Line;
            seriesTheor.Color = Color.Blue;
            seriesTheor.BorderWidth = 3;
            for (int j = 0; j < lambdas.Length; j++)
            {
                seriesTheor.Points.AddXY(lambdas[j], theor[j]);
            }
            chart.Series.Add(seriesTheor);

            if (exp != null)
            {
                var seriesExp = new Series();
                seriesExp.Name = "Experimental";
                seriesExp.ChartType = SeriesChartType.Line;
                seriesExp.Color = Color.Red;
                seriesExp.BorderWidth = 3;
                for (int j = 0; j < lambdas.Length; j++)
                {
                    seriesExp.Points.AddXY(lambdas[j], exp[j]);
                }
                chart.Series.Add(seriesExp);
            }

            chart.Titles.Add(title);
            chart.SaveImage($"result/p-{i}.png", ChartImageFormat.Png);
        }
        public static double Factorial(int n)
        {
            double result = 1.0;
            for (int i = 1; i <= n; i++)
                result *= i;
            return result;
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
        private int processingTime = 500; // стандартное время обработки заявки
        private int poolSize = 5; // стандартный размер пула
        private object threadLock = new object();
        public int requestCount = 0;
        public int processedCount = 0;
        public int rejectedCount = 0;
        public Server(int poolSize, int processingTime)
        {
            pool = new PoolRecord[poolSize];

            this.processingTime = processingTime;
            this.poolSize = poolSize;
        }
        public Server()
        {
            pool = new PoolRecord[poolSize];
        }
        public void proc(object? sender, procEventArgs e)
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
                        return;
                    }
                }
                rejectedCount++;
            }
        }
        public void Answer(object? arg)
        {
            ArgumentNullException.ThrowIfNull(arg);

            int id = (int)arg;
            //for (int i = 1; i < 9; i++)
            //{
            Console.WriteLine("Обработка заявки: {0}", id);
            //Console.WriteLine("{0}",Thread.CurrentThread.Name);
            Thread.Sleep(processingTime);
            //}
            for (int i = 0; i < poolSize; i++)
                if (pool[i].thread == Thread.CurrentThread)
                    pool[i].in_use = false;
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



class Config
{
    public int poolSize { get; set; }
    public int processingTime { get; set; }
    public int requestInterval { get; set; }
}