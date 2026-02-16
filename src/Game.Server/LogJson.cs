using System.Globalization;
using System.Text;

namespace Game.Server;

public static class LogJson
{
    public static string TickEntry(int tick, int sessionCount, int messagesIn, int messagesOut, double simStepMs)
    {
        StringBuilder sb = new(capacity: 128);
        sb.Append("{\"tick\":");
        sb.Append(tick.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"sessionCount\":");
        sb.Append(sessionCount.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"messagesIn\":");
        sb.Append(messagesIn.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"messagesOut\":");
        sb.Append(messagesOut.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"simStepMs\":");
        sb.Append(simStepMs.ToString("0.###", CultureInfo.InvariantCulture));
        sb.Append('}');
        return sb.ToString();
    }
}
