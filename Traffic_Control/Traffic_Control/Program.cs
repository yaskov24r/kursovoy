using Serilog;
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Traffic_Control.Entities.Sensors;
using Traffic_Control.Entities;
using System.Linq;
using Traffic_Control.Enums;

namespace AutomatedTrafficManagement
{
    class Program
    {
        private static readonly ConcurrentDictionary<string, Task> tasks =
            new ConcurrentDictionary<string, Task>();
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> tokens =
            new ConcurrentDictionary<string, CancellationTokenSource>();
        private static readonly ConcurrentDictionary<string, double> values =
            new ConcurrentDictionary<string, double>();

        private static Traffic traffic = new Traffic();
        private static HubConnection connection;

        static async Task Main(string[] args)
        {
            // Ініціалізація Serilog для логування в файл
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console() // Логування в консоль
                .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day) // Логування в файл з архівацією за днями
                .CreateLogger();

            try
            {
                // Старт програми
                Log.Information("Програма стартувала.");

                connection = new HubConnectionBuilder()
                    .WithUrl("https://localhost:7045/indicator")
                    .Build();

                await connection.StartAsync();
                Log.Information("Пiдключення до сервера успiшне.");

                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri("https://localhost:7045/api/");
                var result = await client.GetAsync("indicator");
                var content = await result.Content.ReadAsStringAsync();

                var deserializedResult = JsonSerializer.Deserialize<List<IndicatorModel>>(content, new
                    JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var sensorFactories = new Dictionary<string, Func<Sensor>>
                {
                    {"CarSensor1",() => new CarSensor("Number", "No description") },
                    {"CarSensor2",() => new CarSensor("Number", "No description") },
                    {"CarSensor3",() => new CarSensor("Number", "No description") },
                    {"CarSensor4",() => new CarSensor("Number", "No description") },

                    // Додавайте свої датчики
                };

                foreach (var indicator in deserializedResult)
                {
                    if (sensorFactories.TryGetValue(indicator.Name, out var createSensor))
                    {
                        var sensor = createSensor();
                        sensor.Name = indicator.Name;
                        sensor.Description = indicator.Description;
                        traffic.Sensors.Add(sensor);
                    }
                    else
                    {
                        Log.Warning($"Не відомий індикатор: {indicator.Name}");
                    }
                }

                foreach (var model in deserializedResult)
                {
                    AddDataProcessTask(model.Id,
                        model.Value,
                        model.IndicatorValues.LastOrDefault() ?? "0",
                        model);
                }

                connection.On("UpdateTargetValue", (string id, string value) =>
                {
                    tokens.TryGetValue(id, out CancellationTokenSource? token);
                    if (token == null)
                    {
                        Log.Warning($"Не знайдено токен для ID: {id}");
                        return;
                    }

                    token.Cancel();
                    Log.Information($"Задача з ID: {id} скасована. Створення нової задачі.");
                    AddDataProcessTask(Guid.Parse(id), value, "0", new IndicatorModel());
                });

                connection.Closed += async (error) =>
                {
                    Log.Warning("З'єднання було розірвано. Спроба повторного підключення...");
                    await Task.Delay(new Random().Next(10, 11) * 1000);
                    await connection.StartAsync();
                };

                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Програма неочікувано завершилася.");
            }
            finally
            {
                Log.CloseAndFlush(); // Завершення роботи з логером
            }
        }

        private static void AddDataProcessTask(Guid id, string value, string lastValue, IndicatorModel indicatorModel)
        {
            var source = new CancellationTokenSource();

            var task = CreateDataProcessingTask(
                id,
                double.Parse(value),
                 double.Parse(lastValue),
                 source.Token,
                 indicatorModel);

            tasks.TryAdd(id.ToString(), task);
            tokens.TryAdd(id.ToString(), source);
        }

        private static async Task CreateDataProcessingTask(Guid id, double baseValue, double lastValue,
            CancellationToken token, IndicatorModel indicatorModel)
        {
            while (!token.IsCancellationRequested)
            {
                double value = lastValue;
                values.AddOrUpdate(id.ToString(), lastValue, (name, currentValue) =>
                {
                    value = GenerateValue(baseValue, currentValue, indicatorModel);
                    return value;
                });

                await connection.InvokeAsync("SendValue", id.ToString(), value.ToString(), token);
                await Task.Delay(3000, token);
            }

            tasks.Remove(id.ToString(), out _);
        }

        private static double GenerateValue(double targetValue, double currentValue, IndicatorModel indicatorModel)
        {
            traffic.Monitor();

            var value = traffic.Sensors.FirstOrDefault(x => x.Name == indicatorModel.Name);

            return Math.Round(value.Value, 2);
        }
    }
}
