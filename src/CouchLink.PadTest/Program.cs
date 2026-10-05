using CouchLink.Core.Input;
using CouchLink.Core.Pads;
using CouchLink.Pads;

bool check = args.Length > 0 && args[0].Equals("check", StringComparison.OrdinalIgnoreCase);
var countArg = check ? args.Skip(1).FirstOrDefault() : args.FirstOrDefault();
int count = int.TryParse(countArg, out var n) ? Math.Clamp(n, 1, 9) : 9;

if (check)
    return CouchLink.PadTest.CheckMode.Run(count);

if (!ViGEmPadFactory.TryCreate(out var factory, out var error))
{
    Console.Error.WriteLine(error);
    return 1;
}

using (factory)
{
    var pads = new List<IVirtualPad>();
    for (int i = 0; i < count; i++)
        pads.Add(factory!.Create());

    Console.WriteLine($"Created {count} virtual DS4 pads (P2..P{count + 1}).");
    Console.WriteLine("Keys: 2-9 = pulse P2..P9, 0 = pulse P10, A = pulse all in order, Q = quit");
    Console.WriteLine("A pulse holds Cross + left stick RIGHT for 1 second on that pad only.");

    while (true)
    {
        char key = char.ToUpperInvariant(Console.ReadKey(intercept: true).KeyChar);
        if (key == 'Q')
            break;
        if (key == 'A')
        {
            for (int i = 0; i < pads.Count; i++)
                Pulse(pads, i);
            continue;
        }
        int slot = key == '0' ? 10 : key - '0';
        int index = slot - 2;
        if (index >= 0 && index < pads.Count)
            Pulse(pads, index);
    }

    foreach (var pad in pads)
        pad.Dispose();
}
return 0;

static void Pulse(List<IVirtualPad> pads, int index)
{
    Console.WriteLine($"P{index + 2}: Cross + stick right for 1 s");
    pads[index].Apply(PadState.Neutral with { Buttons = PadButtons.Cross, LX = 255 });
    Thread.Sleep(1000);
    pads[index].Apply(PadState.Neutral);
}
