using System.Text.Json;

namespace TradingJournal.Services
{
    public sealed class MuckaRubricProvider
    {
        private readonly IWebHostEnvironment _environment;
        private readonly object _sync = new();
        private JsonElement? _rubric;

        public MuckaRubricProvider(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public JsonElement GetRubric()
        {
            if (_rubric.HasValue)
                return _rubric.Value;

            lock (_sync)
            {
                if (_rubric.HasValue)
                    return _rubric.Value;

                var path = Path.Combine(
                    _environment.ContentRootPath,
                    "Docs",
                    "strategies",
                    "petr-mucka",
                    "rubric.json");

                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Mucka strategy rubric was not found.", path);
                }

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                _rubric = document.RootElement.Clone();
                return _rubric.Value;
            }
        }
    }
}
