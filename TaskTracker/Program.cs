using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Serialization;
namespace TaskTracker
{
    class Program
    {
        public static void Main(string[] args)
        {
            if(args.Length <= 0)
            {
                Console.WriteLine("Запуск программы: tasktracker.exe yourprog.exe\nНажмите любую кнопку для выхода");
                Console.ReadKey();
                return;
            }
            var progpath = args[0];
            if (!Regex.IsMatch(progpath, @"\w*.exe$")) return; // проверка на корректность введеной строки
            var xmlSerega = new XmlSerializer(typeof(RunInfo));
            RunInfo runinfo = null;
            try
            {
                using (var fs = new FileStream("runinfo.xml", FileMode.Open))
                {
                    runinfo = xmlSerega.Deserialize(fs) as RunInfo;
                }
            }
            catch { }

            runinfo = runinfo ?? new RunInfo();
            
            var xmlSerega2 = new XmlSerializer(typeof(EventLog));
            using(var fslog = new FileStream("processlog.xml", FileMode.Append))
            {
                try
                {
                    using (var process = Process.Start(new ProcessStartInfo { FileName = progpath, UseShellExecute = false }))
                    {
                        bool limitsExceeded = false; // маркер, отображающий вышел ли процесс по причине превышения лимитов
                        while (!process.HasExited)
                        {
                            EventLog logentry;
                           process.Refresh();
                            if (CheckLimits(runinfo, process, LimitType.All)) // превышение лимитов
                            {
                                logentry = CreateLogEntry(EventType.LimitError, process, runinfo);
                                fslog.Write(EntryToByteArray(logentry, xmlSerega2));
                                process.Kill();
                                limitsExceeded = true;
                            }
                            else if (CheckLimits(runinfo, process, LimitType.All, false)) // приближение по лимитам 
                            {
                                logentry = CreateLogEntry(EventType.Warning, process, runinfo);
                                fslog.Write(EntryToByteArray(logentry, xmlSerega2));
                            }
                            else
                            {
                                logentry = CreateLogEntry(EventType.Notification, process);
                                fslog.Write(EntryToByteArray(logentry, xmlSerega2));
                            }
                            Thread.Sleep(100);
                        }
                        if (!limitsExceeded)
                            fslog.Write(EntryToByteArray(CreateLogEntry((process.ExitCode == 0) ? EventType.Exit : EventType.InternalError, process), xmlSerega2));
                    }
                }
                catch(Exception e)
                {
                    Console.WriteLine(e.Message);
                }

            }
            Console.ReadKey();
        }
        /// <summary>
        /// Метод конвертирующий запись в лог в массив байтов для последующего логирования
        /// </summary>
        /// <param name="logentry">Запись</param>
        /// <param name="serializer">Сериализатор</param>
        /// <returns></returns>
        public static byte[] EntryToByteArray(EventLog logentry, XmlSerializer serializer)
        {
            using(var memStream = new MemoryStream())
            {
                serializer.Serialize(memStream, logentry);
                return memStream.ToArray();
            }
        } 
        
        /// <summary>
        /// Метод проверки лимитов
        /// </summary>
        /// <param name="runInfo">Модель с лимитами</param>
        /// <param name="proc">Контекст процесса</param>
        /// <param name="limitType">Тип лимита <seealso cref="LimitType"></param>
        /// <param name="ExceedOrAproach">true если проверяем на превышение, false если проверяем на приближение</param>
        /// <returns></returns>
        public static bool CheckLimits(RunInfo runInfo, Process proc, LimitType limitType, bool ExceedOrAproach=true)
        {
            if (!ExceedOrAproach)
            {
                return limitType switch {
                    LimitType.MemoryLimit => (double)proc.PrivateMemorySize64 / runInfo.MemoryLimit >= 0.9,
                    LimitType.ProcessorTimeLimit => (double)proc.TotalProcessorTime.TotalMilliseconds / runInfo.ProcessorTimeLimit >= 0.9,
                    LimitType.AbsoluteTimelimit => (DateTime.Now - proc.StartTime).TotalMilliseconds / runInfo.AbsoluteTimeLimit >= 0.9,
                    LimitType.All => CheckLimits(runInfo, proc, LimitType.MemoryLimit, false) ||
                                     CheckLimits(runInfo, proc, LimitType.ProcessorTimeLimit, false) ||
                                     CheckLimits(runInfo, proc, LimitType.AbsoluteTimelimit, false)

                };
            }
            else
            {
                return limitType switch
                {
                    LimitType.MemoryLimit => proc.PrivateMemorySize64 > runInfo.MemoryLimit,
                    LimitType.ProcessorTimeLimit => proc.TotalProcessorTime.TotalMilliseconds > runInfo.ProcessorTimeLimit,
                    LimitType.AbsoluteTimelimit => (uint)(DateTime.Now - proc.StartTime).TotalMilliseconds > runInfo.AbsoluteTimeLimit,
                    LimitType.All => CheckLimits(runInfo, proc, LimitType.MemoryLimit) ||
                                     CheckLimits(runInfo, proc, LimitType.ProcessorTimeLimit) ||
                                     CheckLimits(runInfo, proc, LimitType.AbsoluteTimelimit)
                };
            }
        }
        /// <summary>
        /// Метод создающий на основе типа события соответствующую запись для лог-файла
        /// </summary>
        /// <param name="logtype">Тип события</param>
        /// <param name="process">Контекст прцоесса</param>
        /// <param name="runInfo">Модель с лимитами, по умолчанию null, используется если нужно отобразить акцент на лимиты в предупреждении</param>
        /// <returns>Экземпляр записи для лог-файла</returns>
        public static EventLog CreateLogEntry(EventType logtype, Process process, RunInfo runInfo=null)
        {
            var eventLog = new EventLog
                            { 
                              Type = logtype,
                              ProcessId = process.Id,
                              ProcessName = process.ProcessName,
                              Time = DateTime.Now 
                            };

            switch (logtype)
            {
                case EventType.LimitError:
                    eventLog.Message = "Лимиты превышены милорд. Процесс остановлен." +
                            ((runInfo != null) ? "Превышенные лимиты: " + GetExceedingParams(runInfo,process):"");
                    return eventLog;
                case EventType.Notification:
                    eventLog.Message = $"Процесс в порядке милорд, прикладываю статистику - " +
                        $"Память: {(double)process.PagedMemorySize64 / Math.Pow(2, 20)} МБ. " +
                        $"Процессорное время: {process.TotalProcessorTime.TotalMilliseconds} мс." +
                        $"Общее время исполнения: {(DateTime.Now - process.StartTime).TotalMilliseconds} мс.";
                    return eventLog;
                case EventType.Warning:
                    eventLog.Message = $"Милорд, процесс скоро превысит лимиты. Текущая статистика - " +
                        $"Память: {(double)process.PagedMemorySize64 / Math.Pow(2, 20)} МБ. " +
                        $"Процессорное время: {process.TotalProcessorTime.TotalMilliseconds} мс." +
                        $"Общее время исполнения: {(DateTime.Now - process.StartTime).TotalMilliseconds} мс." +
                        ((runInfo != null) ? $"Превышения могут возникуть по следующим параметрам: " + GetExceedingParams(runInfo,process):"");
                    return eventLog;
                case EventType.InternalError:
                    eventLog.Message = $"Милорд, процесс завершился с ошибкой, код ошибки {process.ExitCode}";
                    return eventLog;
                default:
                    eventLog.Message = "Милорд, прцоесс завершился с кодом 0";
                    return eventLog;

            }
        }
        /// <summary>
        /// Метод возвращающий информацию о типах лимитов, которые могут превысить допустимые значения
        /// </summary>
        /// <param name="runInfo">Модель с лимитами</param>
        /// <param name="proc">Контекст процесса</param>
        /// <returns>Строка с акцентом на лимиты приближающиеся к ограничению</returns>
        public static string GetExceedingParams(RunInfo runInfo, Process proc)
        {
            string result="";
            result += CheckLimits(runInfo,proc,LimitType.MemoryLimit,false) ? "Оперативная память " : "";
            result += CheckLimits(runInfo, proc, LimitType.ProcessorTimeLimit, false) ? "Время процессора " : "";
            result += CheckLimits(runInfo, proc, LimitType.AbsoluteTimelimit, false) ? "Время исполнения " : "";
            return result;
        }
    }
    /// <summary>
    /// Перечисление для типов журналирумых событий
    /// </summary>
    public enum EventType
    {
        InternalError,
        LimitError,
        Warning,
        Notification,
        Exit
    }

    /// <summary>
    /// Перечисление для типов ограничений
    /// </summary>
    public enum LimitType
    {
        MemoryLimit,
        ProcessorTimeLimit,
        AbsoluteTimelimit,
        All
    }

    /// <summary>
    /// Модель с ограничениями для процесса
    /// </summary>
    public class RunInfo
    {
        public long MemoryLimit { get; set; }
        public uint ProcessorTimeLimit { get; set; }
        public uint AbsoluteTimeLimit { get; set; }
        public RunInfo()
        {
            MemoryLimit = 268435456;    // значения по умолчанию
            ProcessorTimeLimit = (uint)Math.Pow(10, 4);
            AbsoluteTimeLimit = (uint)Math.Pow(10, 4);
        }
    }

    /// <summary>
    /// Модель записи в логах
    /// </summary>
    public class EventLog
    {
        public EventType Type { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string Message { get; set; }
        public DateTime Time { get; set; }
    }
}
