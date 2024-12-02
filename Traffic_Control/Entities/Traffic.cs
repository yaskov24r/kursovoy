using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Traffic_Control.Entities 
{
    public class Traffic
    {
        public List<Sensors.Sensor> Sensors { get; set; }



        public Traffic()
        {
            Sensors = new List<Sensors.Sensor>();


        }

        public void Monitor()
        {
            foreach (var sensor in Sensors)
            {
                sensor.ReadValue();
            }
        }
    }
}
