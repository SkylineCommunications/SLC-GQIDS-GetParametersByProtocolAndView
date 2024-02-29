namespace GetParametersByProtocolAndView_1
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Skyline.DataMiner.Analytics.GenericInterface;
    using Skyline.DataMiner.Net;
    using Skyline.DataMiner.Net.Messages;

    [GQIMetaData(Name = "Get Parameters by View")]
    public class MyDataSource : IGQIDataSource, IGQIInputArguments, IGQIOnInit
    {
        private GQIDMS _dms;

        private GQIStringArgument protocolArgument = new GQIStringArgument("Protocol") { IsRequired = true };
        private GQIStringArgument versionArgument = new GQIStringArgument("Version") { IsRequired = true, DefaultValue = "Production" };
        private GQIStringArgument numericParamsArgument = new GQIStringArgument("Numeric Parameters") { IsRequired = false }; // ;-separated list of PIDs
        private GQIStringArgument textParamsArgument = new GQIStringArgument("Text Parameters") { IsRequired = false }; // ;-separated list of PIDs
        private GQIStringArgument viewIDArgument = new GQIStringArgument("View ID") { IsRequired = false, DefaultValue = "-1" };
        private GQIBooleanArgument recursiveViewsArgument = new GQIBooleanArgument("Recursive Views") { DefaultValue = false };
        private GQIBooleanArgument getHistoryArgument = new GQIBooleanArgument("Retrieve History") { DefaultValue = false };
        private GQIBooleanArgument getAlarmStateArgument = new GQIBooleanArgument("Add Alarm State") { DefaultValue = false };

        private string protocol;
        private string version;
        private List<int> numericParams;
        private List<int> textParams;
        private int viewID;
        private bool recursiveViews;
        private bool getHistory;
        private bool getAlarmState;
        private GetProtocolInfoResponseMessage protocolInfo;

        private List<GQIColumn> _columns;

        public GQIColumn[] GetColumns()
        {
            return _columns.ToArray();
        }

        public GQIArgument[] GetInputArguments()
        {
            return new GQIArgument[] { protocolArgument, versionArgument, numericParamsArgument, textParamsArgument, viewIDArgument, recursiveViewsArgument, getHistoryArgument, getAlarmStateArgument };
        }

        public GQIPage GetNextPage(GetNextPageInputArgs args)
        {
            var rows = new List<GQIRow>();

            GetLiteElementInfo getLiteElementMessage = new GetLiteElementInfo(false)
            {
                ProtocolName = protocol,
                ProtocolVersion = version,
                ViewID = viewID,
                ExcludeSubViews = !recursiveViews,
            };

            var elementsInfo = _dms.SendMessages(getLiteElementMessage).Cast<LiteElementInfoEvent>().ToList();

            foreach (var element in elementsInfo)
            {
                List<GQICell> cells = new List<GQICell>();

                string elementKey = element.DataMinerID + "/" + element.ElementID;

                cells.Add(new GQICell() { Value = elementKey });
                cells.Add(new GQICell() { Value = element.Name });

                foreach (int param in numericParams)
                {
                    GetParameterMessage getParameterMessage = new GetParameterMessage(element.DataMinerID, element.ElementID, param);
                    var response = (GetParameterResponseMessage)_dms.SendMessage(getParameterMessage);

                    cells.Add(new GQICell() { Value = response.Value.DoubleValue, DisplayValue = response.Value.DoubleValue + " " + protocolInfo.FindParameter(param).Units });

                    if(getAlarmState)
                    {
                        cells.Add(new GQICell() { Value = response.ActualAlarmLevel.ToString() });
                    }

                    if (getHistory)
                    {
                        GetTrendDataMessage getTrendDataMessage = new GetTrendDataMessage(element.DataMinerID, element.ElementID, param)
                        {
                            StartTime = DateTime.Now.AddMonths(-1),
                            EndTime = DateTime.Now.AddMonths(-1).AddSeconds(1),
                            TrendingType = TrendingType.Average,
                            AverageTrendIntervalType = AverageTrendIntervalType.OneDay,
                        };
                        var historyResponse = (GetTrendDataResponseMessage)_dms.SendMessage(getTrendDataMessage);

                        if (historyResponse.Values.Length > 4)
                        {
                            cells.Add(new GQICell() { Value = Convert.ToDouble(historyResponse.Values[5], CultureInfo.InvariantCulture), DisplayValue = Convert.ToDouble(historyResponse.Values[5], CultureInfo.InvariantCulture) + " " + protocolInfo.FindParameter(param).Units });
                        }
                        else // No History available
                        {
                            cells.Add(new GQICell() { Value = null });
                        }
                    }
                }

                foreach (int param in textParams)
                {
                    GetParameterMessage getParameterMessage = new GetParameterMessage(element.DataMinerID, element.ElementID, param);
                    var response = (GetParameterResponseMessage)_dms.SendMessage(getParameterMessage);
                    cells.Add(new GQICell() { Value = response.DisplayValue });
                }

                GQIRow myRow = new GQIRow(elementKey, cells.ToArray());
                rows.Add(myRow);
            }

            return new GQIPage(rows.ToArray())
            {
                HasNextPage = false,
            };
        }

        public OnArgumentsProcessedOutputArgs OnArgumentsProcessed(OnArgumentsProcessedInputArgs args)
        {
            _columns = new List<GQIColumn>
              {
                 new GQIStringColumn("Element ID"),
                 new GQIStringColumn("Element Name"),
              };

            protocol = args.GetArgumentValue(protocolArgument);
            version = args.GetArgumentValue(versionArgument);
            viewID = Convert.ToInt32(args.GetArgumentValue(viewIDArgument));
            recursiveViews = args.GetArgumentValue(recursiveViewsArgument);
            getHistory = args.GetArgumentValue(getHistoryArgument);
            getAlarmState = args.GetArgumentValue(getAlarmStateArgument);

            numericParams = args.GetArgumentValue(numericParamsArgument).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(Int32.Parse).ToList();
            textParams = args.GetArgumentValue(textParamsArgument).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(Int32.Parse).ToList();

            GetProtocolMessage getProtocolMessage = new GetProtocolMessage(protocol, version);
            protocolInfo = (GetProtocolInfoResponseMessage)_dms.SendMessage(getProtocolMessage);

            foreach (int param in numericParams)
            {
                string paramName = protocolInfo.GetParameterName(param);
                _columns.Add(new GQIDoubleColumn(paramName));

                if(getAlarmState)
                {
                    _columns.Add(new GQIStringColumn(paramName + " (Alarm State)"));
                }

                if(getHistory)
                {
                    _columns.Add(new GQIDoubleColumn(paramName + " (Last Month)"));
                }
            }

            foreach (int param in textParams)
            {
                string paramName = protocolInfo.GetParameterName(param);
                _columns.Add(new GQIStringColumn(paramName));
            }

            return new OnArgumentsProcessedOutputArgs();
        }

        public OnInitOutputArgs OnInit(OnInitInputArgs args)
        {
            _dms = args.DMS;
            return new OnInitOutputArgs();
        }
    }
}