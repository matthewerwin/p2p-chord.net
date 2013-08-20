using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chordian
{
    class Program
    {
        static void Main(string[] args)
        {
            LocalPeer lp1 = new LocalPeer(8000);
            LocalPeer lp2 = new LocalPeer(8001);
            LocalPeer lp3 = new LocalPeer(8002);
            LocalPeer lp4 = new LocalPeer(8003);
            LocalPeer lp5 = new LocalPeer(8004);

            Task p1 = lp1.Join(lp2);
            Task p2 = lp2.Join(lp1);
            Task p3 = lp3.Join(lp1);
            Task p4 = lp4.Join(lp2);
            Task p5 = lp5.Join(lp3);

            //Task.WaitAll(p1, p2);



            while (Console.ReadLine() != "q")
            {
                GC.Collect();
                Console.WriteLine(lp1 + " SUCCESSOR: " + lp1.Successor);
                Console.WriteLine(lp1 + " PREDECESSOR: " + lp1.Predecessor);
                Console.WriteLine();
                Console.WriteLine(lp2 + " SUCCESSOR: " + lp2.Successor);
                Console.WriteLine(lp2 + " PREDECESSOR: " + lp2.Predecessor);
            }
        }
    }
}
