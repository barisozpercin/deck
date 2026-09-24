using Deck.Shell.Calendars;

namespace Deck.Shell.Widgets;

/// <summary>A month at a glance, with a dot on any day that has something on it.</summary>
internal sealed class MonthWidget(WidgetContext context) : WidgetBase(context, "month")
{
    /// <summary>Months away from today's month; 0 is this month.</summary>
    private int _offset;

    private DateTime _today = DateTime.Today;

    public override void Start()
    {
        Context.Calendar.Updated += Push;
        Context.Tick.Ticked += OnTick;
        Context.Calendar.Acquire();
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        Context.Calendar.Updated -= Push;
        Context.Tick.Ticked -= OnTick;
        Context.Calendar.Release();
        Context.Tick.Release();
    }

    public override bool Handle(string message)
    {
        switch (message)
        {
            case "prev":
                _offset--;
                break;

            case "next":
                _offset++;
                break;

            case "today":
                _offset = 0;
                break;

            default:
                return false;
        }

        Push();
        return true;
    }

    public override void Push()
    {
        var shown = new DateTime(_today.Year, _today.Month, 1).AddMonths(_offset);
        var first = MonthGrid.FirstCell(shown.Year, shown.Month);
        var entries = Context.Calendar.Entries(first, first.AddDays(MonthGrid.Cells));
        var view = MonthGrid.Build(shown.Year, shown.Month, _today, entries);

        Post(new
        {
            title = view.Title,
            status = Context.Calendar.Health,
            days = view.Days.Select(d => new
            {
                day = d.Date.Day,
                inMonth = d.InMonth,
                today = d.IsToday,
                events = d.Events
            })
        });
    }

    /// <summary>Only midnight changes anything; checked every tick because a desktop can sleep straight through it.</summary>
    private void OnTick()
    {
        if (DateTime.Today == _today) return;

        _today = DateTime.Today;
        Push();
    }
}
