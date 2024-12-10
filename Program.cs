using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;

class Program
{
    static string access_token = "bTU3LUdPLU5aNEZka21ZOHRqYWk0QzNmblZVcm43a2N1clB3T056TU1oST0";

    static async Task Main()
    {
        try
        {
            var tickers = System.IO.File.ReadAllLines("C:/Users/uriyd/Downloads/ticker.txt");
            var tasks = tickers.Select(FetchAndStoreData).ToList();
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Произошла ошибка: {ex.Message}");
        }
    }

    static async Task FetchAndStoreData(string tickerSymbol)
    {
        try
        {
            using (var context = new AppDbContext())
            {
                var startDate = new DateTimeOffset(DateTime.Now.AddDays(-90)).ToUnixTimeSeconds();
                var endDate = new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds();
                var url = $"https://api.marketdata.app/v1/stocks/candles/D/{tickerSymbol}/?from={startDate}&to={endDate}";

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access_token);
                    var response = await client.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Ошибка API для тикера {tickerSymbol}: {response.StatusCode}");
                        return;
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    JObject json = JObject.Parse(content);

                    if (json["h"] == null || json["l"] == null)
                    {
                        Console.WriteLine($"Нет данных для тикера - {tickerSymbol}");
                        return;
                    }

                    double[] h = json["h"].ToObject<double[]>();
                    double[] l = json["l"].ToObject<double[]>();

                    if (h.Length != l.Length || h.Length == 0)
                    {
                        Console.WriteLine($"Некорректные данные для тикера - {tickerSymbol}");
                        return;
                    }

                    // Получаем или создаем тикер
                    var ticker = await context.Ticker.FirstOrDefaultAsync(t => t.TickerName == tickerSymbol)
                                 ?? new Tickers { TickerName = tickerSymbol };

                    if (ticker.Id == 0)
                    {
                        context.Ticker.Add(ticker);
                        await context.SaveChangesAsync();
                    }

                    // Добавляем данные о ценах
                    var prices_ = h.Select((high, i) => new Prices
                    {
                        TickerId = ticker.Id,
                        Price = (decimal)((high + l[i]) / 2),
                        Date = DateTime.Now.AddDays(-90 + i)
                    }).ToList();

                    await context.Prices.AddRangeAsync(prices_);

                    // Рассчитываем состояние
                    var todayPrice = (decimal)((h.Last() + l.Last()) / 2);
                    var yesterdayPrice = (decimal)((h[^2] + l[^2]) / 2);
                    var state = todayPrice > yesterdayPrice ? "Up" : "Down";

                    var todaysCondition = new TodaysCondition
                    {
                        TickerId = ticker.Id,
                        State = state
                    };

                    context.TodaysCondition.Add(todaysCondition);
                    await context.SaveChangesAsync();

                    Console.WriteLine($"Тикер {tickerSymbol}: сегодня цена {(state == "Up" ? "выросла" : "упала")}.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка для тикера {tickerSymbol}: {ex.Message}");
        }
    }
}

public class AppDbContext : DbContext
{
    public DbSet<Tickers> Ticker { get; set; }
    public DbSet<Prices> Prices { get; set; }
    public DbSet<TodaysCondition> TodaysCondition { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer("Server=localhost,1433;Database=StockMarket;User Id=SA;Password=YourStrong!Password;Encrypt=False;TrustServerCertificate=True;");
    }
}

public class Tickers
{
    public int Id { get; set; }
    public string TickerName { get; set; }
}

public class Prices
{
    public int Id { get; set; }
    public int TickerId { get; set; }
    public decimal Price { get; set; }
    public DateTime Date { get; set; }
}

public class TodaysCondition
{
    public int Id { get; set; }
    public int TickerId { get; set; }
    public string State { get; set; }
}
