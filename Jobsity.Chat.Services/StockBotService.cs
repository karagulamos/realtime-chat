using EasyNetQ;
using Jobsity.Chat.Core.Common;
using Jobsity.Chat.Core.Models;
using Jobsity.Chat.Core.Models.Dtos;
using Jobsity.Chat.Core.Services;
using Jobsity.Chat.Services.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

using static Jobsity.Chat.Core.Common.Constants;

namespace Jobsity.Chat.Services;

public class StockBotService : IStockBotService
{
    private readonly ILogger<ChatHub> _logger;
    private readonly IBus _bus;
    private readonly IStockTickerService _stockTickerService;
    private readonly IHubContext<ChatHub> _hubContext;

    public StockBotService(ILogger<ChatHub> logger, IBus bus, IStockTickerService stockTickerService, IHubContext<ChatHub> hubContext)
    {
        _logger = logger;
        _bus = bus;
        _stockTickerService = stockTickerService;
        _hubContext = hubContext;
    }

    public bool FoundValidCommand(string message) => message.Replace(" ", "").StartsWith("/stock=");

    public async Task<bool> TryEnqueueAsync(string message, string roomId, string connectionId)
    {
        var tokens = message.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length != 2)
            return false;

        var request = new StockBotRequest(tokens[1].Trim(), roomId, connectionId);

        await _bus.PubSub.PublishAsync(request);

        await _bus.PubSub.SubscribeAsync(string.Empty, async (StockBotRequest request) =>
        {
            await _hubContext.Groups.AddToGroupAsync(request.ConnectionId, request.RoomId);

            try
            {
                var stockPrice = await _stockTickerService.GetStockPriceAsync(request.StockCode);

                if (stockPrice == default)
                    return;

                var userChat = new UserChatDto(Constants.StockBotId, $"{stockPrice.Code.ToUpper()} quote is ${stockPrice.Price} per share.", DateTime.Now);

                await _hubContext.Clients.Client(request.ConnectionId).SendAsync(ClientReceiveNewMessage, userChat);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing stock bot request");

                var userChat = new UserChatDto(Constants.StockBotId, Constants.BotUnableToProcessRequest, DateTime.Now);
                await _hubContext.Clients.Client(request.ConnectionId).SendAsync(ClientReceiveNewMessage, userChat);
            }
        });

        return true;
    }
}