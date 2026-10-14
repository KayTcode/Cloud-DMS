using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CleanArchCqrs.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
namespace CloudDms.IntegrationTests.Files;
// A protocol peer tests adapter parsing; these are not real ClamAV engine acceptance tests.
public sealed class ClamAvProtocolTests
{
    private static ClamAvVirusScannerService Scanner(Dictionary<string,string?> config)=>new(new ConfigurationBuilder().AddInMemoryCollection(config).Build(),new Microsoft.Extensions.Hosting.Internal.HostingEnvironment(),NullLogger<ClamAvVirusScannerService>.Instance);
    [Theory]
    [InlineData(false,false,100,"SCAN_DISABLED")]
    [InlineData(true,false,100,"SCAN_COVERAGE_UNVERIFIED")]
    [InlineData(true,true,2147483648L,"SCAN_COVERAGE_UNSUPPORTED")]
    public async Task MissingOrImpossibleCoverageNeverReportsClean(bool enabled,bool confirmed,long coverage,string code)
    {
        using var stream=new MemoryStream([1,2,3]);
        var result=await Scanner(new(){["ClamAV:Enabled"]=enabled.ToString(),["SearchIndex:ClamAvCoverageConfirmed"]=confirmed.ToString(),["SearchIndex:MaxScanBytes"]=coverage.ToString()}).ScanStreamAsync(stream);
        Assert.False(result.ScanSucceeded);Assert.Equal(code,result.ThreatName);
    }
    [Theory]
    [InlineData("stream: OK",true,false,null)]
    [InlineData("stream: Eicar-Test-Signature FOUND",true,true,"MALWARE_DETECTED")]
    [InlineData("stream: Heuristics.Limits.Exceeded.MaxFileSize FOUND",false,false,"SCAN_RESOURCE_LIMIT")]
    [InlineData("INSTREAM size limit exceeded. ERROR",false,false,"SCAN_PROTOCOL_OR_LIMIT")]
    [InlineData("untrusted OK",false,false,"SCAN_PROTOCOL_OR_LIMIT")]
    public async Task ParsesBoundedDaemonReplyAndRestoresPosition(string reply,bool succeeded,bool infected,string? code)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));var ct=timeout.Token;
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
        var peer=Task.Run(async()=>{
            using var client=await listener.AcceptTcpClientAsync(ct);await using var network=client.GetStream();
            var header=new byte[10];await network.ReadExactlyAsync(header,ct);Assert.Equal("zINSTREAM\0",Encoding.ASCII.GetString(header));
            var size=new byte[4];while(true){await network.ReadExactlyAsync(size,ct);var length=BinaryPrimitives.ReadInt32BigEndian(size);if(length==0)break;Assert.InRange(length,1,8192);await network.ReadExactlyAsync(new byte[length],ct);}
            await network.WriteAsync(Encoding.ASCII.GetBytes(reply+"\0"),ct);
        },ct);
        using var stream=new MemoryStream([0,1,2,3]);stream.Position=1;
        var result=await Scanner(new(){["ClamAV:Enabled"]="true",["ClamAV:Host"]="127.0.0.1",["ClamAV:Port"]=port.ToString(),["SearchIndex:ClamAvCoverageConfirmed"]="true",["SearchIndex:MaxScanBytes"]="100"}).ScanStreamAsync(stream,ct);
        await peer;Assert.Equal(succeeded,result.ScanSucceeded);Assert.Equal(infected,result.IsInfected);Assert.Equal(code,result.ThreatName);Assert.Equal(1,stream.Position);
    }
}
