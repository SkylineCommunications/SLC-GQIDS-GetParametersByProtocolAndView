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
        private GQIStringArgument propertiesArgument = new GQIStringArgument("Properties") { IsRequired = false }; // ;-separated list of properties
        private GQIStringArgument viewIDArgument = new GQIStringArgument("View ID") { IsRequired = false, DefaultValue = "-1" };
        private GQIBooleanArgument recursiveViewsArgument = new GQIBooleanArgument("Recursive Views") { DefaultValue = false };
        private GQIBooleanArgument getHistoryArgument = new GQIBooleanArgument("Retrieve History") { DefaultValue = false };
        private GQIBooleanArgument getAlarmStateArgument = new GQIBooleanArgument("Add Alarm State") { DefaultValue = false };

        private string protocol;
        private string version;
        private List<int> numericParams;
        private List<int> textParams;
        private List<string> properties;
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
            return new GQIArgument[] { protocolArgument, versionArgument, numericParamsArgument, textParamsArgument, propertiesArgument, viewIDArgument, recursiveViewsArgument, getHistoryArgument, getAlarmStateArgument };
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

                    if (getAlarmState)
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
                            Fields = new string[] { "chValueAvg" },
                        };
                        var historyResponse = (GetTrendDataResponseMessage)_dms.SendMessage(getTrendDataMessage);

                        if (historyResponse.Values.Length > 0)
                        {
                            cells.Add(new GQICell() { Value = Convert.ToDouble(historyResponse.Values[0], CultureInfo.InvariantCulture), DisplayValue = Convert.ToDouble(historyResponse.Values[0], CultureInfo.InvariantCulture) + " " + protocolInfo.FindParameter(param).Units });
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

                if (properties.Count>0)
                {
                    ElementInfoEventMessage propertiesResponse = null;
                    GetElementByIDMessage GetElementByIDMessage = new GetElementByIDMessage(element.DataMinerID, element.ElementID);
                    propertiesResponse = (ElementInfoEventMessage)_dms.SendMessage(GetElementByIDMessage);
                
                    foreach (string property in properties)
                    {
                        string propertyValue = string.Empty;
                        try
                        {
                            propertyValue = propertiesResponse.Properties.Single(p => p.Name == property).Value;
                        }
                        catch (Exception)
                        {

                        }
                        
                        cells.Add(new GQICell() { Value = propertyValue });
                    }
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
            properties = args.GetArgumentValue(propertiesArgument).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            GetProtocolMessage getProtocolMessage = new GetProtocolMessage(protocol, version);
            protocolInfo = (GetProtocolInfoResponseMessage)_dms.SendMessage(getProtocolMessage);

            foreach (int param in numericParams)
            {
                string paramName = protocolInfo.GetParameterName(param);
                _columns.Add(new GQIDoubleColumn(paramName));

                if (getAlarmState)
                {
                    _columns.Add(new GQIStringColumn(paramName + " (Alarm State)"));
                }

                if (getHistory)
                {
                    _columns.Add(new GQIDoubleColumn(paramName + " (Last Month)"));
                }
            }

            foreach (int param in textParams)
            {
                string paramName = protocolInfo.GetParameterName(param);
                _columns.Add(new GQIStringColumn(paramName));
            }

            foreach (string property in properties)
            {
                _columns.Add(new GQIStringColumn(property));
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