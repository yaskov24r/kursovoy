using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Traffic_Control.Entities.Sensors;
using Traffic_Control.Entities;
using System.Reflection;
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
            connection = new HubConnectionBuilder()
                .WithUrl("https://localhost:7045/indicator")
                .Build();

            await connection.StartAsync();

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

                // Add your sensors here
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
                    Console.WriteLine($"Unknown indicator: {indicator.Name}");
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
                if (tokens == null)
                {
                    Console.WriteLine($"No token found for ID: {id}");
                    return;
                }

                token.Cancel();
                Console.WriteLine($"CancellingTask with ID: {id} and adding new task");
                AddDataProcessTask(Guid.Parse(id), value, "0", new IndicatorModel());
            });

            connection.Closed += async (error) =>
            {
                Console.WriteLine("Connection closed. Trying to reconnect ...");
                await Task.Delay(new Random().Next(10, 11) * 1000);
                await connection.StartAsync();
            };

            Console.ReadLine();
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
