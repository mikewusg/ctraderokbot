using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;
using System.Threading.Tasks;
using System.Net;
using System.Collections.Generic;
using System.Timers;
using System.Web.Script.Serialization;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class OKBOT : Robot
    {

        protected override void OnStart()
        {
            Timer.Start(5);
        }

        protected override void OnTimer()
        {
            OnTimedEvent();
        }

        private void OnTimedEvent()
        {

            using (WebClient wc = new WebClient())
            {

                try
                {


                    wc.Headers.Add("Content-Type", "application/json");
                    wc.Headers[HttpRequestHeader.Accept] = "application/json";


                    string openPositionsJson = wc.DownloadString("http://localhost:9045/position/latest-open-positions");
                    string closePositionsJson = wc.DownloadString("http://localhost:9045/position/latest-close-positions");

                    JavaScriptSerializer json_serializer = new JavaScriptSerializer();



                    List<OpenPositionRequest> openPositionList = (List<OpenPositionRequest>)json_serializer.Deserialize(openPositionsJson, typeof(List<OpenPositionRequest>));
                    List<ClosePositionRequest> closePositionList = (List<ClosePositionRequest>)json_serializer.Deserialize(closePositionsJson, typeof(List<ClosePositionRequest>));



                    foreach (var openPosition in openPositionList)
                    {
                        openPositionRequest(openPosition);
                    }


                    foreach (var closePosition in closePositionList)
                    {
                        closePositionRequest(closePosition);
                    }

                } catch (WebException e)
                {
                    Print("Exception: {0}", e.Message);
                }

            }
        }

        private void openPositionRequest(OpenPositionRequest request)
        {
            try
            {
                var positionInfo = request.positionInfo;

                foreach (var p in Positions)
                {
                    if (string.Equals(positionInfo.symbol, p.SymbolName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        throw new Exception("There is already an open position for symbol: " + p.SymbolName);
                    }
                }

                var tradeType = TradeType.Buy;

                if (string.Equals(request.positionInfo.tradeType, "SELL", StringComparison.InvariantCultureIgnoreCase))
                {
                    tradeType = TradeType.Sell;
                }
                else if (string.Equals(request.positionInfo.tradeType, "BUY", StringComparison.InvariantCultureIgnoreCase))
                {
                    tradeType = TradeType.Buy;
                }
                else
                {
                    throw new Exception("Invalid trade type: " + request.positionInfo.tradeType);
                }



                double sl = 30;
                double tp = 20;


                var result = ExecuteMarketOrder(tradeType, positionInfo.symbol, Symbol.QuantityToVolumeInUnits(positionInfo.quantity * getCoeficient(positionInfo.symbol)), positionInfo.label, sl, tp, positionInfo.comment);

                if (result.IsSuccessful)
                {
                    sendMessageToTelegram(new PositionResponse(request.messageId, "Result:\n" + result.ToString() + "\n\nPosition:\n" + result.Position.ToString(), true));
                }
                else
                {
                    sendMessageToTelegram(new PositionResponse(request.messageId, result.ToString(), false));
                }

            } catch (Exception e)
            {
                Print("Exception: {0}", e.Message);
                sendMessageToTelegram(new PositionResponse(request.messageId, "Exception on opening position.\nMessage: " + e.Message, false));
            }
        }

        private int getCoeficient(string symbol)
        {
            var historyList = new List<HistoricalTrade>();

            foreach (HistoricalTrade trade in History)
            {
                if (string.Equals(symbol, trade.SymbolName))
                {
                    historyList.Add(trade);
                }
            }


            int failureCount = 0;

            for (int i = historyList.Count - 1; i >= 0; i--)
            {
                HistoricalTrade trade = historyList[i];
                if (trade.GrossProfit < 0)
                {
                    failureCount++;
                }
                else
                {
                    break;
                }
            }

            if (failureCount == 0)
                return 1;
            if (failureCount == 1)
                return 2;
            if (failureCount == 2)
                return 4;
            if (failureCount == 3)
                return 8;
            return 1;

        }

        private void closePositionRequest(ClosePositionRequest request)
        {
            try
            {
                Position position = Positions.Find(request.label);
                if (position == null)
                {

                    sendMessageToTelegram(new PositionResponse(request.messageId, "Position not found with label:\n" + request.label, false));
                }
                else
                {
                    TradeResult result = ClosePosition(position);
                    if (result.IsSuccessful)
                    {
                        sendMessageToTelegram(new PositionResponse(request.messageId, "Result:\n" + result.ToString() + "\n\nPosition:\n" + result.Position.ToString() + "\n\nNet Profit: " + result.Position.NetProfit + "\n\nProfit in Pips: " + result.Position.Pips, true));
                    }
                    else
                    {

                        sendMessageToTelegram(new PositionResponse(request.messageId, result.ToString(), false));
                    }
                }
            } catch (Exception e)
            {
                Print("Exception: {0}", e.Message);

                sendMessageToTelegram(new PositionResponse(request.messageId, "Exception on closing position.\nMessage: " + e.Message, false));

            }
        }

        private void sendMessageToTelegram(PositionResponse res)
        {
            try
            {

                using (WebClient wc = new WebClient())
                {

                    wc.Headers.Add("Content-Type", "application/json");
                    wc.Headers[HttpRequestHeader.Accept] = "application/json";

                    JavaScriptSerializer serializer = new JavaScriptSerializer();

                    var jsonString = serializer.Serialize(res);





                    wc.UploadString("http://localhost:9045/position/respond", jsonString);

                }

            } catch (Exception e)
            {
                Print("Exception: {0}", e.Message);
            }
        }



    }


    class PositionRequest
    {

        public MessageAction messageAction { get; set; }
        public long messageId { get; set; }

        public override string ToString()
        {
            return "messageAction: " + messageAction + ", messageId: " + messageId;
        }

    }


    class ClosePositionRequest : PositionRequest
    {
        public string label { get; set; }
        public override string ToString()
        {
            return "messageAction: " + messageAction + ", messageId: " + messageId + ", label: " + label;
        }

    }

    class OpenPositionRequest : PositionRequest
    {
        public PositionInfo positionInfo { get; set; }


        public override string ToString()
        {
            return "messageAction: " + messageAction + ", messageId: " + messageId + ", positionInfo: " + positionInfo;
        }

    }

    class PositionInfo
    {
        public string symbol { get; set; }
        public string tradeType { get; set; }
        public double quantity { get; set; }
        public string label { get; set; }
        public double entryPrice { get; set; }
        public double? takeProfit { get; set; }
        public double stopLoss { get; set; }
        public string comment { get; set; }

    }


    enum MessageAction
    {
        OPEN,
        CLOSE
    }


    class PositionResponse
    {
        public long replyToMessageId { get; set; }
        public string message { get; set; }
        public bool ok { get; set; }

        public PositionResponse()
        {

        }

        public PositionResponse(long replyToMessageId, string message, bool ok)
        {
            this.replyToMessageId = replyToMessageId;
            this.message = message;
            this.ok = ok;
        }

    }
}
