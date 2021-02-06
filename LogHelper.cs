using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kernys.Bson;

namespace PixelWorldsServer
{
    class LogHelper
    {
        public static void LogBSONInDepth(BSONObject bObj, bool appendToFile = false)
        {
            string data = "";
            foreach (string key in bObj.Keys)
            {
                BSONValue bVal = bObj[key];

                switch (bVal.valueType)
                {
                    case BSONValue.ValueType.String:
                        data += "[GETWORLD] >> KEY: " + key + " VALUE: " + bVal.stringValue + "\n";
                        break;
                    case BSONValue.ValueType.Object:
                        {
                            if (bVal is BSONObject)
                            {
                                data += "[GETWORLD] >> KEY: " + key + " VALUE: (is bsonobject)\n";
                                LogBSONInDepth(bVal as BSONObject, true);
                            }
                            else
                            {
                                data += "[GETWORLD] >> KEY: " + key + " VALUE: (is object)\n";
                            }
                            // that object related shit is more complex so im gonna leave that for later
                            break;
                        }
                    case BSONValue.ValueType.Array:
                        {
                            /*List<int> wBlocks = bVal.int32ListValue;
                            foreach (int x in wBlocks)
                            {
                                Console.WriteLine(x);
                            }*/
                            data += "[GETWORLD] >> KEY: " + key + " VALUE: (is array)\n";
                            break;
                        }
                    case BSONValue.ValueType.Int32:
                        data += "[GETWORLD] >> KEY: " + key + " VALUE: " + bVal.int32Value.ToString() + "\n";
                        break;
                    case BSONValue.ValueType.Int64:
                        data += "[GETWORLD] >> KEY: " + key + " VALUE: " + bVal.int64Value.ToString() + "\n";
                        break;
                    case BSONValue.ValueType.Double:
                        data += "[GETWORLD] >> KEY: " + key + " VALUE: " + bVal.doubleValue.ToString() + "\n";
                        break;
                    case BSONValue.ValueType.Boolean:
                        data += "[GETWORLD] >> KEY: " + key + " VALUE: " + bVal.boolValue.ToString() + "\n";
                        break;
                    default:
                        data += "[GETWORLD] >> KEY: " + key + "\n";
                        break;
                }
            }
            Console.WriteLine(data);
            File.AppendAllText("bsonlogs.txt", data);
        }
    }
}
