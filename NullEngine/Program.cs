using NullEngine.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine
{
    public class Program
    {
        static void Main(string[] args)
        {
            using (var window = new MainWindow())
            {
                window.Run();
            }
        }
    }
}
