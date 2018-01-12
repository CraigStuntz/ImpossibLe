using System;
using System.Reflection;

namespace ImpossibLe
{
    class Program
    {
        static UInt64 SumNonTailRecursive(UInt64 n)
        {
            if (n < 2)
            {
                return n;
            }
            else
            {
                return n + SumNonTailRecursive(n - 1);
            }
        }

        static UInt64 SumTailRecursive(UInt64 n, UInt64 accum)
        {
            if (n < 1)
            {
                return accum;
            }
            else
            {
                return SumTailRecursive(n - 1, n + accum);
            }
        }

        static UInt64 WillCrash()
        {
            return SumTailRecursive(50000, 0);
        }

        static UInt64 WillNotCrash()
        {
            var tailCallVersion = TailCall.Rewrite<UInt64, UInt64>(null, SumTailRecursive);
            // This will still crash when run in the debugger! Run from the command line 
            // to see the glorious, non-crashy behavior.
            return tailCallVersion(50000, 0);
        }

        static void Main(string[] args)
        {
            try
            {
                UInt64 result = WillNotCrash();
                Console.WriteLine(result.ToString());
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
                Console.ReadLine();
            }
        }


    }
}
