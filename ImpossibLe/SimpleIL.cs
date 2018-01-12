using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ImpossibLe
{
    class SimpleIL
    {
        static int Add1(int number)
        {
            return number + 1;
        }

        static int Add1Log(int number)
        {
            try
            {
                return number + 1;
            }
            finally
            {
                Debug.Write("All done!");
            }
        }

        static void EmptyGuid()
        {
            var x = Guid.Empty;
            Console.WriteLine(x.ToString());
        }

        static void UseObject()
        {
            var x = new Object();
            Console.WriteLine(x.ToString());
        }

        static void UseArray()
        {
            var x = new int[1];
            x[0] = 1;
            Console.WriteLine(x[0]);
        }
    }
}
