using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AutoFilesTransfer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string configPath = "config.json";
            Config config = LoadConfig(configPath);

            if (config == null)
            {
                Console.WriteLine("Конфигурация не найдена. Создаю новый файл конфигурации...");
                config = new Config
                {
                    SourceDirs = new List<string> { @"e:\FilesNota\572149\1", @"e:\FilesNota\572149\2" },
                    TargetDirs = new List<string> { @"\\192.168.2.15\5otd\Test\{DATA}\1", @"\\192.168.2.15\5otd\Test\{DATA}\2" },
                    MinFileSize = 26463150,
                    DateTemplate = "??ГГГГ?ММ?ДД",
                    LogRetentionDays = 5,
                    TimerIntervalMinutes = 10,
                    EnableTimer = false
                };

                SaveConfig(configPath, config);
                Console.WriteLine("Конфигурация создана. Отредактируйте config.json и перезапустите программу.");
                return;
            }

            if (config.SourceDirs.Count != config.TargetDirs.Count)
            {
                Console.WriteLine("Ошибка: количество исходных и целевых папок должно совпадать.");
                return;
            }

            Directory.CreateDirectory("logs");

            if (config.EnableTimer)
            {
                Timer timer = new Timer(_ => ProcessFiles(config), null, TimeSpan.Zero, TimeSpan.FromMinutes(config.TimerIntervalMinutes));
                Console.WriteLine($"Программа запущена с таймером. Интервал: {config.TimerIntervalMinutes} минут.");
                Log("Программа запущена с таймером.");
                Console.ReadLine();
            }
            else
            {
                Console.WriteLine("Программа запущена без таймера (однократная обработка).");
                Log("Программа запущена без таймера.");
                ProcessFiles(config);
                Console.WriteLine("Завершено. Окно закроется через 5 секунд...");
                Log("Завершено. Ожидание 5 секунд перед закрытием окна.");
                Thread.Sleep(5000);
            }
        }

        static void ProcessFiles(Config config)
        {
            try
            {
                for (int i = 0; i < config.SourceDirs.Count; i++)
                {
                    string sourceDir = config.SourceDirs[i];
                    string targetTemplate = config.TargetDirs[i];

                    if (!Directory.Exists(sourceDir))
                    {
                        Log($"Исходная папка не найдена: {sourceDir}");
                        continue;
                    }

                    var allFiles = Directory.GetFiles(sourceDir);
                    var bigFiles = allFiles.Where(f => new FileInfo(f).Length >= config.MinFileSize).ToList();
                    var smallFiles = allFiles.Where(f => new FileInfo(f).Length < config.MinFileSize).ToList();

                    if (!bigFiles.Any())
                    {
                        Log($"Нет файлов >= {config.MinFileSize} байт в {sourceDir}. Пропуск.");
                        continue;
                    }

                    Log(
                    $"В директории {sourceDir} найдено:\n" +
                    $"- файлов превышающих заданный размер: {bigFiles.Count}\n" +
                    $"- файлов не превышающих заданный размер: {smallFiles.Count}"
                    );

                    var filesToCopy = bigFiles.Concat(smallFiles).ToList();

                    foreach (var file in filesToCopy)
                    {
                        string fileName = Path.GetFileName(file);
                        string dateFolder = ExtractDateFromFileName(fileName, config.DateTemplate);

                        if (dateFolder == "UnknownDate")
                        {
                            Log($"Пропущен файл {fileName} — не удалось извлечь дату.");
                            continue;
                        }

                        string targetDir = targetTemplate.Replace("{DATA}", dateFolder);
                        string targetPath = Path.Combine(targetDir, fileName);

                        try
                        {
                            Directory.CreateDirectory(targetDir);
                            File.Copy(file, targetPath, true);
                            Log($"Файл скопирован: {file} → {targetPath}");

                            FileInfo src = new FileInfo(file);
                            FileInfo dst = new FileInfo(targetPath);

                            if (src.Length == dst.Length)
                            {
                                File.Delete(file);
                                Log($"Исходный файл удалён: {file}");
                            }
                            else
                            {
                                Log($"Размеры не совпадают, файл не удалён: {file}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Ошибка при копировании файла {fileName}: {ex.Message}");
                        }
                    }

                    CleanOldLogs(config);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при обработке файлов: {ex.Message}");
            }
        }

        static string ExtractDateFromFileName(string fileName, string template)
        {
            try
            {
                int yearPos = template.IndexOf("ГГГГ");
                int monthPos = template.IndexOf("ММ");
                int dayPos = template.IndexOf("ДД");

                if (yearPos == -1 || monthPos == -1 || dayPos == -1)
                    throw new FormatException("Неверный шаблон даты");

                string year = fileName.Substring(yearPos, 4);
                string month = fileName.Substring(monthPos, 2);
                string day = fileName.Substring(dayPos, 2);

                return $"{day}-{month}-{year}";
            }
            catch (Exception ex)
            {
                Log($"Ошибка извлечения даты из имени файла {fileName}: {ex.Message}");
                return "UnknownDate";
            }
        }

        static void CleanOldLogs(Config config)
        {
            string logDir = "logs";
            var oldLogs = Directory.GetFiles(logDir)
                .Where(f => new FileInfo(f).CreationTime < DateTime.Now.AddDays(-config.LogRetentionDays))
                .ToList();

            foreach (var log in oldLogs)
            {
                try
                {
                    File.Delete(log);
                    Log($"Удалён старый лог: {log}");
                }
                catch (Exception ex)
                {
                    Log($"Ошибка при удалении лога {log}: {ex.Message}");
                }
            }
        }

        static void Log(string message)
        {
            string logDir = "logs";
            string logFile = Path.Combine(logDir, $"log_{DateTime.Now:yyyy-MM-dd}.txt");
            string timeStamped = $"{DateTime.Now:HH:mm:ss} - {message}";

            Console.WriteLine(timeStamped);
            File.AppendAllText(logFile, timeStamped + Environment.NewLine);
        }

        static Config LoadConfig(string path)
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<Config>(json);
        }

        static void SaveConfig(string path, Config config)
        {
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(path, json);
        }
    }

    public class Config
    {
        public List<string> SourceDirs { get; set; }
        public List<string> TargetDirs { get; set; }
        public long MinFileSize { get; set; }
        public string DateTemplate { get; set; }
        public int LogRetentionDays { get; set; }
        public int TimerIntervalMinutes { get; set; }
        public bool EnableTimer { get; set; }
    }
}