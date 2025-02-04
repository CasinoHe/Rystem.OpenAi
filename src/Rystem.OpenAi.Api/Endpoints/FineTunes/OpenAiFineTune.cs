﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Rystem.OpenAi.FineTune
{
    internal sealed class OpenAiFineTune : OpenAiBase, IOpenAiFineTune
    {
        private readonly bool _forced;
        public OpenAiFineTune(IHttpClientFactory httpClientFactory,
            IEnumerable<OpenAiConfiguration> configurations,
            IOpenAiUtility utility) 
            : base(httpClientFactory, configurations, utility)
        {
            _forced = false;
        }
        public FineTuneRequestBuilder Create(string trainingFileId)
            => new FineTuneRequestBuilder(Client, _configuration, trainingFileId, Utility);
        public ValueTask<FineTuneResults> ListAsync(CancellationToken cancellationToken = default)
            => Client.GetAsync<FineTuneResults>(_configuration.GetUri(OpenAiType.FineTune, string.Empty, _forced, string.Empty), _configuration, cancellationToken);
        public ValueTask<FineTuneResult> RetrieveAsync(string fineTuneId, CancellationToken cancellationToken = default)
            => Client.GetAsync<FineTuneResult>(_configuration.GetUri(OpenAiType.FineTune, fineTuneId, _forced, $"/{fineTuneId}"), _configuration, cancellationToken);
        public ValueTask<FineTuneResult> CancelAsync(string fineTuneId, CancellationToken cancellationToken = default)
            => Client.PostAsync<FineTuneResult>(_configuration.GetUri(OpenAiType.FineTune, fineTuneId, _forced, $"/{fineTuneId}/cancel"), null, _configuration, cancellationToken);
        public ValueTask<FineTuneEventsResult> ListEventsAsync(string fineTuneId, CancellationToken cancellationToken = default)
            => Client.GetAsync<FineTuneEventsResult>(_configuration.GetUri(OpenAiType.FineTune, fineTuneId, _forced, $"/{fineTuneId}/events"), _configuration, cancellationToken);
        public ValueTask<FineTuneDeleteResult> DeleteAsync(string fineTuneId, CancellationToken cancellationToken = default)
            => Client.DeleteAsync<FineTuneDeleteResult>(_configuration.GetUri(OpenAiType.FineTune, fineTuneId, _forced, $"/{fineTuneId}"), _configuration, cancellationToken);
        public IAsyncEnumerable<FineTuneResult> ListAsStreamAsync(CancellationToken cancellationToken = default)
            => Client.StreamAsync(_configuration.GetUri(OpenAiType.FineTune, string.Empty, _forced, string.Empty), null, HttpMethod.Get, _configuration, ReadFineTuneStreamAsync, cancellationToken);
        public IAsyncEnumerable<FineTuneEvent> ListEventsAsStreamAsync(string fineTuneId, CancellationToken cancellationToken = default)
            => Client.StreamAsync(_configuration.GetUri(OpenAiType.FineTune, string.Empty, _forced, $"/{fineTuneId}/events"), null, HttpMethod.Get, _configuration, ReadEventsStreamAsync, cancellationToken);
        private static IAsyncEnumerable<FineTuneResult> ReadFineTuneStreamAsync(Stream stream, HttpResponseMessage response, CancellationToken cancellationToken)
        {
            return ReadStreamAsync(stream, response, bufferAsString =>
            {
                var chunkResponse = JsonSerializer.Deserialize<FineTuneResults>(bufferAsString);
                var chunk = chunkResponse!.Data?.LastOrDefault();
                return chunk!;
            }, cancellationToken);
        }
        private static IAsyncEnumerable<FineTuneEvent> ReadEventsStreamAsync(Stream stream, HttpResponseMessage response, CancellationToken cancellationToken)
        {
            return ReadStreamAsync(stream, response, bufferAsString =>
            {
                var chunkResponse = JsonSerializer.Deserialize<FineTuneEventsResult>(bufferAsString);
                var chunk = chunkResponse!.Data?.LastOrDefault();
                return chunk!;
            }, cancellationToken);
        }
        private static async IAsyncEnumerable<T> ReadStreamAsync<T>(Stream stream, HttpResponseMessage response, Func<string, T> entityReader, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var reader = new StreamReader(stream);
            string line;
            var buffer = new StringBuilder();
            var curlyCounter = 0;
            var squareCounter = 0;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (line.StartsWith(Constants.StartingWith))
                    line = line[Constants.StartingWith.Length..];
                if (line == Constants.Done)
                {
                    yield break;
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    buffer.AppendLine(line);
                    var openCurlyCounter = line.Count(x => x == '{');
                    var openSquareCounter = line.Count(x => x == '[');
                    var closeCurlyCounter = line.Count(x => x == '}');
                    var closeSquareCounter = line.Count(x => x == ']');
                    curlyCounter += openCurlyCounter - closeCurlyCounter;
                    squareCounter += openSquareCounter - closeSquareCounter;
                    if (curlyCounter + squareCounter == 2)
                    {
                        var bufferAsString = $"{buffer.ToString().Trim().Trim(',')}{new string(']', squareCounter)}{new string('}', curlyCounter)}";
                        var chunk = entityReader(bufferAsString);
                        if (chunk != null)
                        {
                            if (chunk is ApiBaseResponse apiResult)
                                apiResult.SetHeaders(response);
                            yield return chunk;
                        }
                    }
                }
            }
        }
    }
}
