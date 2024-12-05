using Traffic_Control.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Traffic_Control.Entities.Sensors
{
    public class CarSensor : Sensor
    {
        public override SensorType Type => SensorType.CarSensor;

        public CarSensor(string name, string description)
            : base (name, description) { }

        public override void ReadValue()
        {
            Value = new Random().Next(20, 60);
            Console.WriteLine($"{Name} Speed: {Value}");
        }
    }
}