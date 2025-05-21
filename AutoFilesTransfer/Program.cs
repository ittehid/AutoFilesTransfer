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
                Log("[INFO] Файл конфигурации не найден. Создаю новый...");
                
                config = new Config
                {
                    Rules = new List<SyncRule>
                {
                    new SyncRule
                    {
                        SourceDir = @"e:\FilesNota\572149\1",
                        TargetDir = @"\\192.168.2.15\5otd\Test\{DATA}\1",
                        MinFileSize = 26463150,
                        DateTemplate = "??ГГГГ?ММ?ДД"
                    },
                    new SyncRule
                    {
                        SourceDir = @"e:\FilesNota\572149\2",
                        TargetDir = @"\\192.168.2.15\Test\Test\{DATA}\2",
                        MinFileSize = 26463150,
                        DateTemplate = "??ГГГГ?ММ?ДД"
                    }
                },
                    LogRetentionDays = 5,
                    TimerIntervalMinutes = 10,
                    EnableTimer = false
                };

                SaveConfig(configPath, config);

                Log("[INFO] Файл конфигурации создан. Отредактируйте config.json и перезапустите программу.");
                return;
            }

            if (config.Rules == null || config.Rules.Count == 0)
            {
                Log("[ERROR] Ошибка: не задано ни одного правила синхронизации.");
                return;
            }

            Directory.CreateDirectory("logs");

            if (config.EnableTimer)
            {
                Timer timer = new Timer(_ => ProcessFiles(config), null, TimeSpan.Zero, TimeSpan.FromMinutes(config.TimerIntervalMinutes));
                Log($"[INFO] Программа запущена с таймером. Интервал: {config.TimerIntervalMinutes} минут.");                
                Console.ReadLine();
            }
            else
            {
                Log("[INFO] Программа запущена без таймера.");
                ProcessFiles(config);
                Log("[INFO] Завершено. Ожидание 5 секунд перед закрытием окна.");
                Thread.Sleep(5000);
            }
        }

        static void ProcessFiles(Config config)
        {
            try
            {
                foreach (var rule in config.Rules)
                {
                    string sourceDir = rule.SourceDir;
                    string targetTemplate = rule.TargetDir;

                    if (!Directory.Exists(sourceDir))
                    {
                        Log($"[INFO] Исходная папка не найдена: {sourceDir}");
                        continue;
                    }

                    var allFiles = Directory.GetFiles(sourceDir);
                    var bigFiles = allFiles.Where(f => new FileInfo(f).Length >= rule.MinFileSize).ToList();
                    var smallFiles = allFiles.Where(f => new FileInfo(f).Length < rule.MinFileSize).ToList();

                    if (!bigFiles.Any())
                    {
                        Log($"[INFO] Нет файлов >= {rule.MinFileSize} байт в {sourceDir}. Пропуск.");
                        continue;
                    }

                    Log(
                    $"---------------------------------------\n" +
                    $"Проверка директории:\n" +
                    $"{sourceDir}\n" +
                    $"Найдено:\n" +
                    $"- файлов заданного размера: {bigFiles.Count}\n" +
                    $"- файлов меньшего размера: {smallFiles.Count}\n\n"
                    );

                    var filesToCopy = bigFiles.Concat(smallFiles).ToList();

                    foreach (var file in filesToCopy)
                    {
                        string fileName = Path.GetFileName(file);
                        string dateFolder = ExtractDateFromFileName(fileName, rule.DateTemplate);

                        if (dateFolder == "UnknownDate")
                        {
                            Log($"[INFO] Пропущен файл {fileName} — не удалось извлечь дату.");
                            continue;
                        }

                        string targetDir = targetTemplate.Replace("{DATA}", dateFolder);
                        string targetPath = Path.Combine(targetDir, fileName);

                        try
                        {
                            Directory.CreateDirectory(targetDir);
                            File.Copy(file, targetPath, true);
                            Log($"[INFO] Файл скопирован: {file} → {targetPath}");

                            FileInfo src = new FileInfo(file);
                            FileInfo dst = new FileInfo(targetPath);

                            if (src.Length == dst.Length)
                            {
                                File.Delete(file);
                                Log($"[INFO] Исходный файл удалён: {file}");
                            }
                            else
                            {
                                Log($"[INFO] Размеры не совпадают, файл не удалён: {file}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"[ERROR] Ошибка при копировании файла {fileName}: {ex.Message}");
                        }
                    }

                    CleanOldLogs(config);
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Ошибка при обработке файлов: {ex.Message}");
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
                Log($"[ERROR] Ошибка извлечения даты из имени файла {fileName}: {ex.Message}");
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
                    Log($"[INFO] Удалён старый лог: {log}");
                }
                catch (Exception ex)
                {
                    Log($"[ERROR] Ошибка при удалении лога {log}: {ex.Message}");
                }
            }
        }

        static void Log(string message)
        {
            string logDir = "logs";
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            string logFile = Path.Combine(logDir, $"{DateTime.Now:dd-MM-yyyy}_log.txt");
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
        public List<SyncRule> Rules { get; set; }
        public int LogRetentionDays { get; set; }
        public int TimerIntervalMinutes { get; set; }
        public bool EnableTimer { get; set; }
    }

    public class SyncRule
    {
        public string SourceDir { get; set; }
        public string TargetDir { get; set; }
        public long MinFileSize { get; set; }
        public string DateTemplate { get; set; }
    }
}