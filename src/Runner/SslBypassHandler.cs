using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Runner;

/// <summary>
/// DelegatingHandler that bypasses SSL certificate validation for local kind clusters
/// </summary>
public class SslBypassHandler : DelegatingHandler
{
    public SslBypassHandler() : base(new SocketsHttpHandler
    {
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
        }
    })
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        return base.SendAsync(request, cancellationToken);
    }
}
