﻿using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Prometheus;
using XLWebServices.Services;
using XLWebServices.Services.PluginData;

namespace XLWebServices.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class PluginController : ControllerBase
{
    private readonly ILogger<PluginController> _logger;
    private readonly RedisService _redis;
    private readonly IConfiguration _configuration;
    private readonly PluginDataService _pluginData;

    private static readonly Counter DownloadsOverTime = Metrics.CreateCounter("xl_plugindl", "XIVLauncher Plugin Downloads", "Name", "Testing");

    private const string RedisCumulativeKey = "XLPluginDlCumulative";

    public PluginController(ILogger<PluginController> logger, RedisService redis, IConfiguration configuration, PluginDataService pluginData)
    {
        _logger = logger;
        _redis = redis;
        _configuration = configuration;
        _pluginData = pluginData;
    }

    [HttpGet("{internalName}")]
    public async Task<IActionResult> Download(string internalName, [FromQuery(Name = "branch")] string branch = "master", [FromQuery(Name = "isTesting")] bool isTesting = false)
    {
        await _pluginData.EnsureOrWait();

        if (_pluginData.PluginMaster!.All(x => x.InternalName != internalName))
            return BadRequest("Invalid plugin");

        DownloadsOverTime.WithLabels(internalName.ToLower(), isTesting.ToString()).Inc();

        await _redis.IncrementCount(internalName);
        await _redis.IncrementCount(RedisCumulativeKey);

        const string githubPath = "https://raw.githubusercontent.com/goatcorp/DalamudPlugins/{0}/{1}/{2}/latest.zip";
        var baseUrl = isTesting ? "testing" : "plugins";
        return new RedirectResult(string.Format(githubPath, branch, baseUrl, internalName));
    }

    [HttpGet]
    public async Task<Dictionary<string, long>> GetDownloadCounts()
    {
        await _pluginData.EnsureOrWait();

        var counts = new Dictionary<string, long>();
        foreach (var plugin in _pluginData.PluginMaster!)
        {
            counts.Add(plugin.InternalName, await _redis.GetCount(plugin.InternalName));
        }

        return counts;
    }

    [HttpGet]
    public async Task<IActionResult> GetPluginMaster()
    {
        await _pluginData.EnsureOrWait();

        return Content(JsonSerializer.Serialize(this._pluginData.PluginMaster, new JsonSerializerOptions
        {
        }), "application/json");
    }

    [HttpGet]
    public async Task<IActionResult> ClearCache([FromQuery] string key)
    {
        if (key != _configuration["CacheClearKey"])
            return BadRequest();

        await _pluginData.ClearCache();

        return Ok();
    }

    [HttpGet]
    public async Task<PluginMeta> Meta()
    {
        await _pluginData.EnsureOrWait();

        return new PluginMeta
        {
            NumPlugins = _pluginData.PluginMaster!.Count,
            LastUpdate = _pluginData.LastUpdate,
            CumulativeDownloads = await _redis.GetCount(RedisCumulativeKey),
        };
    }

    public class PluginMeta
    {
        public int NumPlugins { get; init; }
        public long CumulativeDownloads { get; init; }
        public DateTime LastUpdate { get; init; }
    }
}