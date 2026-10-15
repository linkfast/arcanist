using System.ComponentModel;
using Arcanist.Core.Application.Libraries;
using GraphQL;

namespace Arcanist.Core.Api.GraphQL;

public sealed partial class Mutation
{
    [Name("testS3Connection")]
    [Description("Checks that an S3 bucket answers with those keys, and how much music it holds. Needs the Cloud plan. Administrators only.")]
    public static Task<S3ConnectionTest> TestS3Connection(S3ConnectionInput input, [FromServices] S3LibraryService s3, CancellationToken cancellationToken) =>
        s3.TestAsync(input, cancellationToken);

    [Name("createS3Library")]
    [Description("A library in an S3 bucket (any S3-compatible service). Needs the Cloud plan. Administrators only, with a recent authentication.")]
    public static Task<Library> CreateS3Library(CreateS3LibraryInput input, [FromServices] S3LibraryService s3, CancellationToken cancellationToken) =>
        s3.CreateAsync(input, cancellationToken);
}
