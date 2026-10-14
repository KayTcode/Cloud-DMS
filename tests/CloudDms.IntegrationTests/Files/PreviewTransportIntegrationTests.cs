using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CloudDms.IntegrationTests.Support;
namespace CloudDms.IntegrationTests.Files;
[Collection("Search services")]
public sealed class PreviewTransportIntegrationTests(SearchServicesFixture services)
{
    private const string Mime="application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private static byte[] Docx(bool macro=false)
    {
        using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        {
            void Entry(string name,string content){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(content);}
            Entry("[Content_Types].xml","<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>");
            Entry("word/document.xml","<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>Synthetic preview fixture</w:t></w:r></w:p></w:body></w:document>");
            if(macro)Entry("word/vbaProject.bin","synthetic macro marker");
        }return stream.ToArray();
    }
    private async Task<HttpResponseMessage> Send(byte[] data,string mime=Mime,string version="libreoffice-sandbox-v1",bool badHash=false)
    {
        using var client=new HttpClient {Timeout=TimeSpan.FromSeconds(70)};using var request=new HttpRequestMessage(HttpMethod.Post,services.PreviewEndpoint+"convert"){Content=new ByteArrayContent(data)};
        request.Content.Headers.ContentType=new MediaTypeHeaderValue(mime);request.Headers.Add("X-Converter-Version",version);request.Headers.Add("X-Content-SHA256",badHash?new string('0',64):Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
        return await client.SendAsync(request);
    }
    [Fact]
    public async Task ActualConverterTransportRejectsActiveInputHashMismatchAndVersionMismatch()
    {
        using(var html=await Send("<script>fixture</script>"u8.ToArray(),"text/html"))Assert.Equal(HttpStatusCode.UnprocessableEntity,html.StatusCode);
        using(var version=await Send(Docx(),version:"wrong-version"))Assert.Equal(HttpStatusCode.Conflict,version.StatusCode);
        using(var hash=await Send(Docx(),badHash:true))Assert.Equal(HttpStatusCode.ServiceUnavailable,hash.StatusCode);
        using(var macro=await Send(Docx(true)))Assert.Equal(HttpStatusCode.ServiceUnavailable,macro.StatusCode);
        using var valid=await Send(Docx());
        if(valid.StatusCode==HttpStatusCode.OK)
        {
            Assert.Equal("application/pdf",valid.Content.Headers.ContentType!.MediaType);var pdf=await valid.Content.ReadAsByteArrayAsync();Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        }
        else Assert.Equal(HttpStatusCode.ServiceUnavailable,valid.StatusCode);
        // Record the observed capability; a 503 pass means fail-closed transport only, never successful conversion/layout.
        await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(),"cloud-dms-task12-preview-status.txt"),((int)valid.StatusCode).ToString());
    }
}
