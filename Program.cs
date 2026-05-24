using System;
using System.Threading;
namespace TPProj
{
    class Program
    {  
        static int ThreadsNumber = 5;
        static int Mu = 2;
        static int QueryIntensivityStart = 1;
        static int PlotPoints = 20;
        static int Factorial(int n)
        {
            int factorial = 1;
            for (int j = 2; j <= n; j++)
                factorial *= j;
            return factorial;
        }
        static double CalculateIdleProbability(int queryIntensivity)
        {
            double idleProbabilitySum = 0;
            for (int i = 0; i <= ThreadsNumber; i++)
            {
                double pI = Math.Pow(queryIntensivity, i);
                idleProbabilitySum += pI / Factorial(i);
            }
            return 1 / idleProbabilitySum;
        }
        static void Main()
        {
            int[] xs = new int[PlotPoints];
            double[][] characteristics = new double[10][];
            for (int i = 0; i < 10; i++)
                characteristics[i] = new double[PlotPoints];
            for (int i = QueryIntensivityStart; i < QueryIntensivityStart + PlotPoints; i++)
            {
                int index = i - QueryIntensivityStart;
                xs[index] = i;
                Console.WriteLine("ЭКСПЕРИМЕНТ {0}", index + 1);
                characteristics[0][index] = CalculateIdleProbability(i);
                characteristics[1][index] = characteristics[0][index] * Math.Pow(i, ThreadsNumber) / Factorial(ThreadsNumber);
                characteristics[2][index] = 1 - characteristics[1][index];
                characteristics[3][index] = characteristics[2][index] * i * Mu;
                characteristics[4][index] = characteristics[2][index] * i;
                Server server = new Server(ThreadsNumber, Mu);
                Client client = new Client(server);
                for (int id = 1; id <= 100; id++)
                {
                    client.send(id);
                    Thread.Sleep(1000 / (i * Mu));
                }
                Console.WriteLine("Всего заявок: {0}", server.requestCount);
                Console.WriteLine("Обработано заявок: {0}", server.processedCount);
                Console.WriteLine("Отклонено заявок: {0}", server.rejectedCount);
                characteristics[5][index] = (double)server.idleCount / server.requestCount;
                characteristics[6][index] = (double)server.rejectedCount / server.requestCount;
                characteristics[7][index] = (double)server.processedCount / server.requestCount;
                characteristics[8][index] = Mu * (double)server.threadsCount / server.requestCount;
                characteristics[9][index] = (double)server.threadsCount / server.requestCount;
                Console.WriteLine("Вероятность простоя системы:\nРасчётная: {0}, экспериментальная: {1}", 
                    characteristics[0][index], characteristics[5][index]);
                Console.WriteLine("Вероятность отказа системы:\nРасчётная: {0}, экспериментальная: {1}",
                    characteristics[1][index], characteristics[6][index]);
                Console.WriteLine("Относительная пропускная способность:\nРасчётная: {0}, экспериментальная: {1}",
                    characteristics[2][index], characteristics[7][index]);
                Console.WriteLine("Абсолютная пропуская способность:\nРасчётная: {0}, экспериментальная: {1}",
                    characteristics[3][index], characteristics[8][index]);
                Console.WriteLine("Среднее число занятых каналов:\nРасчётная: {0}, экспериментальная: {1}",
                    characteristics[4][index], characteristics[9][index]);
                Console.WriteLine("------------------------------------------------------------");
            }
            for (int i = 0; i < 5; i++)
            {
                string filename = "p-" + (i + 1).ToString() + ".png";
                ScottPlot.Plot graph = new();
                graph.Add.Scatter(xs, characteristics[i]);
                graph.Add.Scatter(xs, characteristics[i + 5]);
                graph.SavePng(filename, 1024, 1024);
            }
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
        private int Mu;
        private object threadLock = new object();
        public int requestCount = 0;
        public int processedCount = 0;
        public int rejectedCount = 0;
        public int threadsCount = 0;
        public int idleCount = -1;
        public Server(int poolSize, int mu)
        {
            pool = new PoolRecord[poolSize];
            Mu = mu;
        }
        int currentlyActiveThreads()
        {
            int n = 0;
            for (int i = 0; i < pool.Length; i++)
                if (pool[i].in_use)
                    n++;
            return n;
        }
        public void proc(object sender, procEventArgs e)
        {
            lock (threadLock)
            {
                Console.WriteLine("Заявка с номером: {0}", e.id);
                requestCount++;
                int threadsCountNow = currentlyActiveThreads();
                if (threadsCountNow == 0)
                    idleCount++;
                else
                    threadsCount += threadsCountNow;
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
                Console.WriteLine("Заявка {0} отклонена", e.id);
            }
        }
        public void Answer(object arg)
        {
            int id = (int)arg;       
            Console.WriteLine("Обработка заявки: {0}", id);         
            Thread.Sleep(1000/Mu);
            Console.WriteLine("Заявка {0} обработана", id);
            for (int i = 0; i < pool.Length; i++)
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
    public class procEventArgs : EventArgs { public int id { get; set; } }
}
