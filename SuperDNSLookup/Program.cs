using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace SuperDNSLookup
{
    class Program
    {
        static string defaultDoH = "https://dns.pub/dns-query";
        static List<Uri> inputDoHs = new List<Uri>();
        static int inputThreadNum = 32;
        static int dnsTryCount = 3;

        static void Main(string[] args)
        {
            if (args.Length >= 1)
            {
                List<string> inputFilenames = new List<string>();
                string inputDomain = "";

                for (int argi = 0; argi < args.Length; argi++)
                {
                    switch (args[argi])
                    {
                        case "-i":
                        case "--input":
                            {
                                if (args.Length > ++argi)
                                {
                                    if (File.Exists(args[argi]))
                                    {
                                        inputFilenames.Add(args[argi]);
                                    }
                                    else
                                    {
                                        Console.Error.WriteLine($"[E] '{args[argi]}' file is not exists, skip.");
                                    }
                                }
                            }
                            break;
                        case "-h":
                        case "--host":
                            {
                                if (args.Length > ++argi)
                                {
                                    inputDomain = args[argi];
                                }
                            }
                            break;
                        case "-d":
                        case "--dns":
                            {
                                if (args.Length > ++argi)
                                {
                                    Uri newInputDoH = null;
                                    Uri.TryCreate(args[argi], UriKind.Absolute, out newInputDoH);
                                    if (newInputDoH == null)
                                    {
                                        Console.Error.WriteLine($"[E] '{args[argi]}' is not a DoH API Url.");
                                    }
                                    else
                                    {
                                        inputDoHs.Add(newInputDoH);
                                    }
                                }
                            }
                            break;
                        case "-t":
                        case "--thread":
                            {
                                if (args.Length > ++argi)
                                {
                                    int newInputThreadNum = 0;
                                    int.TryParse(args[argi], out newInputThreadNum);
                                    if (newInputThreadNum < 1)
                                    {
                                        Console.Error.WriteLine($"[E] '{args[argi]}' is not a thread count (>= 1).");
                                    }
                                    else
                                    {
                                        inputThreadNum = newInputThreadNum;
                                    }
                                }
                            }
                            break;
                        default:
                            {
                                Console.Error.WriteLine($"[W] Unknown parameter '{args[argi]}'.");
                            }
                            break;
                    }
                }
                if (inputDoHs.Count == 0)
                {
                    inputDoHs.Add(new Uri("https://doh.pub/dns-query", UriKind.Absolute));
                }
                if (inputFilenames.Count > 0 && inputDomain.Length > 0)
                {
                    Dictionary<string, Dictionary<string, long>> filesIpsCountDict = new Dictionary<string, Dictionary<string, long>>();
                    long allFilesLinesCount = 0;
                    long allFilesLines = 0;
                    foreach (string inputFilename in inputFilenames)
                    {
                        string[] cidrLines = File.ReadAllLines(inputFilename);
                        Dictionary<string, long> ipsCountDict = new Dictionary<string, long>();
                        allFilesLines += cidrLines.Length;
                    }
                    foreach (string inputFilename in inputFilenames)
                    {
                        if (filesIpsCountDict.ContainsKey(inputFilename))
                        {
                            Console.Error.WriteLine($"[W] Filename '{inputFilename}' has exists, skip lookup.");
                        }
                        else
                        {
                            string[] cidrLines = File.ReadAllLines(inputFilename);
                            Dictionary<string, long> ipsCountDict = new Dictionary<string, long>();
                            long taskCount = 0;
                            Console.Error.WriteLine("查询：" + inputDomain + " in " + inputFilename);
                            cidrLines.AsParallel().WithDegreeOfParallelism(inputThreadNum).ForAll(cidr =>
                            {
                                lock (cidrLines)
                                {
                                    Console.Error.WriteLine($"{allFilesLinesCount++} / {allFilesLines} ({taskCount++} / {cidrLines.Length})");
                                }
                                foreach (var inputDoH in inputDoHs)
                                {
                                    foreach (var ip in DoHClient(inputDomain, inputDoH.ToString(), cidr))
                                    {
                                        lock (ipsCountDict)
                                        {
                                            if (ipsCountDict.ContainsKey(ip))
                                            {
                                                ipsCountDict[ip]++;
                                            }
                                            else
                                            {
                                                ipsCountDict.Add(ip, 1);
                                            }
                                        }
                                    }
                                }
                            });
                            Console.Error.WriteLine($"{cidrLines.Length} / {cidrLines.Length}");

                            filesIpsCountDict.Add(inputFilename, ipsCountDict);
                        }
                    }
                    Dictionary<string, long> allIpsCountDict = new Dictionary<string, long>();
                    foreach (var filesIpsCountPair in filesIpsCountDict)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"{filesIpsCountPair.Value.Count} 个结果：（{filesIpsCountPair.Key}）");
                        foreach (var ipCountPair in filesIpsCountPair.Value)
                        {
                            var curIp = ipCountPair.Key;
                            var curIpCount = ipCountPair.Value;
                            Console.WriteLine($"{curIp}\t\t\t总计：{curIpCount}");
                            if (allIpsCountDict.ContainsKey(curIp))
                            {
                                allIpsCountDict[curIp] += curIpCount;
                            }
                            else
                            {
                                allIpsCountDict.Add(curIp, curIpCount);
                            }
                        }
                    }
                    Console.WriteLine();
                    Console.WriteLine($"{allIpsCountDict.Count} 个结果：（全部）");
                    foreach (var ipCountPair in allIpsCountDict)
                    {
                        var curIp = ipCountPair.Key;
                        var curIpCount = ipCountPair.Value;
                        Console.WriteLine($"{curIp}\t\t\t总计：{curIpCount}");
                    }
                }
                else
                {
                    PrintHelp();
                }
            }
            else
            {
                PrintHelp();
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine($"SuperDNSLookup Usage");
            Console.WriteLine();
            Console.WriteLine($"-h\t--host\t\t\tDomain or Hostname (MUST)");
            Console.WriteLine($"-i\t--input\t\t\tCIDR address txt file (MUST)");
            Console.WriteLine($"-d\t--dns\t\t\tDNS over HTTPS JSON API url (default: {defaultDoH})");
            Console.WriteLine($"-t\t--thread\t\tThread Number (default: {inputThreadNum})");
        }

        static List<string> DoHClient(string domain, string dohApi, string ecs = "", uint type = 1)
        {
            List<string> IPs = new List<string>();
            string responseText = null;

            for (int i = 0; i < dnsTryCount; i++)
            {
                //if (i > 0)
                //{
                //    Thread.Sleep(500);
                //}
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(dohApi + "?name=" + domain + "&type=" + type.ToString() + "&edns_client_subnet=" + ecs);

                    request.Timeout = 7000;
                    request.ReadWriteTimeout = 7000;

                    // Set some reasonable limits on resources used by this request
                    request.MaximumAutomaticRedirections = 4;
                    request.MaximumResponseHeadersLength = 4;
                    // Set credentials to use for this request.
                    request.Credentials = CredentialCache.DefaultCredentials;
                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();

                    // Get the stream associated with the response.
                    Stream receiveStream = response.GetResponseStream();

                    // Pipes the stream to a higher level stream reader with the required encoding format.
                    StreamReader readStream = new StreamReader(receiveStream, Encoding.UTF8);

                    responseText = readStream.ReadToEnd();

                    response.Close();
                    readStream.Close();

                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine((i + 1).ToString() + "/" + dnsTryCount.ToString() + " Network Error: " + ex.Message + "\n\tUrl: " + dohApi + " (" + domain + ")" + (ex.InnerException == null ? "" : ("\n\tException: " + ex.InnerException)));
                }
            }

            if (responseText != null)
            {
                JObject jObject = JObject.Parse(responseText);

                if (jObject["Answer"] != null)
                {
                    JArray answers = jObject?["Answer"].Value<JArray>();
                    foreach (var answer in answers)
                    {
                        JObject answerObject = answer.Value<JObject>();
                        if (answerObject["type"].ToString() == type.ToString())
                        {
                            IPs.Add(answerObject["data"].ToString());
                        }
                    }
                }
            }

            return IPs;
        }
    }
}
