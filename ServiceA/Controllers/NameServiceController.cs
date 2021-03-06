﻿using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceFabric.Services.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using Polly.Registry;
using Polly;
using ServiceA.Services;
using Microsoft.ServiceFabric.Services.Communication.Client;
using System.Threading;
using Microsoft.Extensions.Http;

namespace ServiceA.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class NameServiceController : ControllerBase
    {
        private readonly ILogger<NameServiceController> _logger;
        private readonly IHttpClientFactory clientFactory;
        private readonly ITypedHttpClientFactory<WeatherClientTyped> _typedHttpClientFactory;
        private readonly IReadOnlyPolicyRegistry<string> _registry;
        private readonly IServiceResolver serviceResolver;
        private readonly ICommunicationClientFactory<ServiceFabricWeatherClient> serviceFabricClientFactory;

        public NameServiceController(ILogger<NameServiceController> logger,
                                    IHttpClientFactory clientFactory,
                                        ITypedHttpClientFactory<WeatherClientTyped> typedHttpClientFactory,
                                        IReadOnlyPolicyRegistry<string> registry,
                                        ILogger<ServiceFabricWeatherClientFactory> sflogger,
                                        IServiceResolver serviceResolver)
        {
            _logger = logger;
            this.clientFactory = clientFactory;
            _typedHttpClientFactory = typedHttpClientFactory;
            _registry = registry;
            this.serviceResolver = serviceResolver;
            this.serviceFabricClientFactory = new ServiceFabricWeatherClientFactory(clientFactory, ServiceFabricWeatherClientFactory.CreateHandlers(sflogger));
        }

        [HttpGet("bad")]
        public async Task<WeatherResults> Get()
        {
            List<WeatherForecast> w1;
            List<WeatherForecast> w2;

            string serviceB = await this.serviceResolver.ResolveService(Constants.serviceB);
            using (var httpClient = new HttpClient { BaseAddress = new Uri(serviceB) })
            {
                var result = await httpClient.GetStringAsync("WeatherForecast").ConfigureAwait(false);
                w1 = JsonConvert.DeserializeObject<List<WeatherForecast>>(result);
            }

            string serviceC = await this.serviceResolver.ResolveService(Constants.serviceB);
            using (var httpClient = new HttpClient { BaseAddress = new Uri(serviceC) })
            {
                var result = await httpClient.GetStringAsync("WeatherForecast").ConfigureAwait(false);
                w2 = JsonConvert.DeserializeObject<List<WeatherForecast>>(result);
            }

            return new WeatherResults { WeatherForecast1 = w1, WeatherForecast2 = w2 };
        }

        [HttpGet("noretry")]
        public async Task<WeatherResults> GetGood(string faults = "false")
        {
            var client = this.clientFactory.CreateClient("noretry");
            var typedclient = this._typedHttpClientFactory.CreateClient(client);

            string serviceB = await this.serviceResolver.ResolveService(Constants.serviceB);
            var w1 = await typedclient.GetWeather($"{serviceB}/WeatherForecast?faults={faults}");

            string serviceC = await this.serviceResolver.ResolveService(Constants.serviceC);
            var w2 = await typedclient.GetWeather($"{serviceC}/WeatherForecast?faults={faults}");

            return new WeatherResults{ WeatherForecast1 = w1, WeatherForecast2 = w2 };
        }

        [HttpGet("polly/retrys")]
        public async Task<WeatherResults> GetRetrys(string faults = "false")
        {
            var client = this.clientFactory.CreateClient("retry");
            var typedclient = this._typedHttpClientFactory.CreateClient(client);

            // todo perform service resolution if non transient
            string serviceB = await this.serviceResolver.ResolveService(Constants.serviceB);
            var w1 = await typedclient.GetWeather($"{serviceB}/WeatherForecast?faults={faults}");

            string serviceC = await this.serviceResolver.ResolveService(Constants.serviceC);
            var w2 = await typedclient.GetWeather($"{serviceC}/WeatherForecast?faults={faults}");

            return new WeatherResults { WeatherForecast1 = w1, WeatherForecast2 = w2 };
        }

        [HttpGet("servicepartion/retrys")]
        public async Task<WeatherResults> GetWithServicePartionClient(string faults = "false")
        {
            var serviceBClient = new ServicePartitionClient<ServiceFabricWeatherClient>(this.serviceFabricClientFactory, new Uri($"fabric:/{Constants.serviceB}"));
            var w1 = await serviceBClient.InvokeWithRetryAsync(async (client) =>
                                {
                                    return await client.CallServiceAsync($"WeatherForecast?faults={faults}").ConfigureAwait(false);
                                }, CancellationToken.None).ConfigureAwait(false);

            var serviceCClient = new ServicePartitionClient<ServiceFabricWeatherClient>(this.serviceFabricClientFactory, new Uri($"fabric:/{Constants.serviceC}"));
            var w2 = await serviceCClient.InvokeWithRetryAsync(async (client) =>
                                {
                                    return await client.CallServiceAsync($"WeatherForecast?faults={faults}").ConfigureAwait(false);
                                }, CancellationToken.None).ConfigureAwait(false);

            return new WeatherResults { WeatherForecast1 = w1, WeatherForecast2 = w2 };
        }

        
    }
}