//var builder = WebApplication.CreateBuilder(args);

//// Add services to the container.
//// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();

//var app = builder.Build();

//// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
//}

//app.UseHttpsRedirection();

//var summaries = new[]
//{
//    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
//};

//app.MapGet("/weatherforecast", () =>
//{
//    var forecast =  Enumerable.Range(1, 5).Select(index =>
//        new WeatherForecast
//        (
//            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
//            Random.Shared.Next(-20, 55),
//            summaries[Random.Shared.Next(summaries.Length)]
//        ))
//        .ToArray();
//    return forecast;
//})
//.WithName("GetWeatherForecast");

//app.Run();

//record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
//{
//    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
//}


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins("http://localhost:5173", "http://localhost:3000", "http://localhost:8080")
        .AllowAnyHeader()
        .AllowAnyMethod());
});
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("Frontend");
app.MapGet("/", () => Results.Ok(new
{
    Service = "ApiGateway",
    Status = "Running",
    Routes = new[]
    {
        "/orders/{**catch-all}",
        "/order-sagas/{**catch-all}",
        "/payments/{**catch-all}",
        "/stock/{**catch-all}",
        "/billing/{**catch-all}",
        "/notifications/{**catch-all}"
    }
}));

app.MapGet("/gateway/health", () => Results.Ok(new
{
    Status = "Healthy",
    Timestamp = DateTimeOffset.UtcNow
}));

app.MapControllers();
app.MapReverseProxy();
app.Run();
