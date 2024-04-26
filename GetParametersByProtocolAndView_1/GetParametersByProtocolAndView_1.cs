namespace GetParametersByProtocolAndView_1
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Skyline.DataMiner.Analytics.GenericInterface;
    using Skyline.DataMiner.Net;
    using Skyline.DataMiner.Net.Messages;
    using Skyline.DataMiner.Net.Trending;

    [GQIMetaData(Name = "Get Parameters by View")]
    public class MyDataSource : IGQIDataSource, IGQIInputArguments, IGQIOnInit
    {
        private GQIDMS _dms;

        private GQIStringArgument protocolArgument = new GQIStringArgument("Protocol") { IsRequired = true };
        private GQIStringArgument versionArgument = new GQIStringArgument("Version") { IsRequired = true, DefaultValue = "Production" };
        private GQIStringArgument primaryKeyArgument = new GQIStringArgument("Primary Key") { IsRequired = false };
        private GQIStringArgument numericParamsArgument = new GQIStringArgument("Numeric Parameters") { IsRequired = false }; // ;-separated list of PIDs
        private GQIStringArgument textParamsArgument = new GQIStringArgument("Text Parameters") { IsRequired = false }; // ;-separated list of PIDs
        private GQIStringArgument propertiesArgument = new GQIStringArgument("Properties") { IsRequired = false }; // ;-separated list of properties
        private GQIStringArgument viewIDArgument = new GQIStringArgument("View ID") { IsRequired = false, DefaultValue = "-1" };
        private GQIIntArgument historyDaysArgument = new GQIIntArgument("History Days") { IsRequired = false, DefaultValue = 1 };
        private GQIBooleanArgument recursiveViewsArgument = new GQIBooleanArgument("Recursive Views") { DefaultValue = false };
        private GQIBooleanArgument getHistoryArgument = new GQIBooleanArgument("Retrieve History") { DefaultValue = false };
        private GQIBooleanArgument getAlarmStateArgument = new GQIBooleanArgument("Add Alarm State") { DefaultValue = false };

        private string protocol;
        private string version;
        private string primaryKey;
        private List<int> numericParams;
        private List<int> textParams;
        private List<string> properties;
        private int viewID;
        private int historyDays;
        private bool recursiveViews;
        private bool getHistory;
        private bool getAlarmState;
        private bool isTableParameter;
        private GetProtocolInfoResponseMessage protocolInfo;

        private List<GQIColumn> _columns;

        public OnInitOutputArgs OnInit(OnInitInputArgs args)
        {
            _dms = args.DMS;
            return new OnInitOutputArgs();
        }

        public GQIColumn[] GetColumns()
        {
            return _columns.ToArray();
        }

        public GQIArgument[] GetInputArguments()
        {
            return new GQIArgument[] { protocolArgument, versionArgument, primaryKeyArgument, numericParamsArgument, textParamsArgument, propertiesArgument, historyDaysArgument, viewIDArgument, recursiveViewsArgument, getHistoryArgument, getAlarmStateArgument };
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
            historyDays = Convert.ToInt32(args.GetArgumentValue(historyDaysArgument));
            getAlarmState = args.GetArgumentValue(getAlarmStateArgument);

            primaryKey = args.GetArgumentValue(primaryKeyArgument);
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
                    _columns.Add(new GQIDoubleColumn(paramName + $"(MIN) (Last {historyDays} Day(s))"));
                    _columns.Add(new GQIDoubleColumn(paramName + $"(AVG) (Last {historyDays} Day(s))"));
                    _columns.Add(new GQIDoubleColumn(paramName + $"(MAX) (Last {historyDays} Day(s))"));
                    _columns.Add(new GQIDoubleColumn(paramName + $"(PREV. AVG) (Last {historyDays} Day(s))"));
                    _columns.Add(new GQIDoubleColumn(paramName + $"(GROWTH) (Last {historyDays} Day(s))"));
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
                    GetParameterResponseMessage response = null;
                    if (string.IsNullOrWhiteSpace(primaryKey))
                    {
                        GetParameterMessage getParameterMessage = new GetParameterMessage(element.DataMinerID, element.ElementID, param);
                        response = (GetParameterResponseMessage)_dms.SendMessage(getParameterMessage);
                    }
                    else
                    {
                        GetParameterMessage getParameterMessage = new GetParameterMessage(element.DataMinerID, element.ElementID, param, primaryKey, usePrimaryKey: true);
                        response = (GetParameterResponseMessage)_dms.SendMessage(getParameterMessage);
                    }

                    cells.Add(new GQICell() { Value = response.Value.DoubleValue, DisplayValue = response.Value.DoubleValue + " " + protocolInfo.FindParameter(param).Units });

                    if (getAlarmState)
                    {
                        cells.Add(new GQICell() { Value = response.ActualAlarmLevel.ToString() });
                    }

                    if (getHistory)
                    {
                        GetTrendDataResponseMessage historyResponse = GetHistoryResponse(element, param, historyDays * -1);

                        if (historyResponse != null && historyResponse.Records.Values.Count > 0)
                        {
                            var firstRecordList = historyResponse.Records.Values.FirstOrDefault();
                            var averageTrendRecord = firstRecordList?.FirstOrDefault() as AverageTrendRecord;

                            var averageTrendRecordValue = Math.Round(Convert.ToDouble(averageTrendRecord?.AverageValue, CultureInfo.InvariantCulture), 2);

                            cells.Add(new GQICell() { Value = Math.Round(Convert.ToDouble(averageTrendRecord?.MinimumValue, CultureInfo.InvariantCulture), 2), DisplayValue = Math.Round(Convert.ToDouble(averageTrendRecord?.MinimumValue, CultureInfo.InvariantCulture), 2) + " " + protocolInfo.FindParameter(param).Units });
                            cells.Add(new GQICell() { Value = averageTrendRecordValue, DisplayValue = averageTrendRecordValue + " " + protocolInfo.FindParameter(param).Units });
                            cells.Add(new GQICell() { Value = Math.Round(Convert.ToDouble(averageTrendRecord?.MaximumValue, CultureInfo.InvariantCulture), 2), DisplayValue = Math.Round(Convert.ToDouble(averageTrendRecord?.MaximumValue, CultureInfo.InvariantCulture), 2) + " " + protocolInfo.FindParameter(param).Units });

                            var previousHistoryResponse = GetHistoryResponse(element, param, (historyDays * -1) * 2); // Getting previous history metrics
                            if (previousHistoryResponse != null)
                            {
                                firstRecordList = previousHistoryResponse.Records.Values.FirstOrDefault();
                                var averagePreviousTrendRecord = firstRecordList?.FirstOrDefault() as AverageTrendRecord;

                                var averagePreviousTrendRecordValue = Math.Round(averagePreviousTrendRecord?.AverageValue ?? 0, 2);

                                cells.Add(new GQICell() { Value = Convert.ToDouble(averagePreviousTrendRecordValue, CultureInfo.InvariantCulture), DisplayValue = Convert.ToDouble(averagePreviousTrendRecordValue, CultureInfo.InvariantCulture) + " " + protocolInfo.FindParameter(param).Units });

                                var increasePercentage = ((averageTrendRecordValue - averagePreviousTrendRecordValue) / averagePreviousTrendRecordValue) * 100;

                                cells.Add(new GQICell() { Value = Math.Round(Convert.ToDouble(increasePercentage, CultureInfo.InvariantCulture), 2), DisplayValue = Math.Round(Convert.ToDouble(increasePercentage, CultureInfo.InvariantCulture), 2) + " %" });
                            }
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

                if (properties.Count > 0)
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

        private GetTrendDataResponseMessage GetHistoryResponse(LiteElementInfoEvent element, int param, int historyDaysToSubstract)
        {
            if (string.IsNullOrWhiteSpace(primaryKey))
            {
                GetTrendDataMessage getTrendDataMessage = new GetTrendDataMessage(element.DataMinerID, element.ElementID, param)
                {
                    StartTime = DateTime.Now.AddDays(historyDaysToSubstract),
                    EndTime = DateTime.Now.AddDays(historyDaysToSubstract).AddDays(1),
                    TrendingType = TrendingType.Average,
                    AverageTrendIntervalType = AverageTrendIntervalType.OneDay,
                    ReturnAsObjects = true,
                    Fields = new string[] { "chValueAvg" },
                };

                return (GetTrendDataResponseMessage)_dms.SendMessage(getTrendDataMessage);
            }
            else
            {
                GetTrendDataMessage getTrendDataMessage = new GetTrendDataMessage(element.DataMinerID, element.ElementID, param, primaryKey)
                {
                    StartTime = DateTime.Now.AddDays(historyDaysToSubstract),
                    EndTime = DateTime.Now.AddDays(historyDaysToSubstract).AddDays(1),
                    TrendingType = TrendingType.Average,
                    AverageTrendIntervalType = AverageTrendIntervalType.OneDay,
                    ReturnAsObjects = true,
                    Fields = new string[] { "chValueAvg" },
                };

                return (GetTrendDataResponseMessage)_dms.SendMessage(getTrendDataMessage);
            }

        }
    }
}