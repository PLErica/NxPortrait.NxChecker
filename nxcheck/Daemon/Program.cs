using NxCheck.Daemon;

var builder = Host.CreateApplicationBuilder(args);

// systemd 연동 — Type=notify, 구조적 로깅(저널)
builder.Services.AddSystemd();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
