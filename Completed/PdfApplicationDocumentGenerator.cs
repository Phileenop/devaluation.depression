using System;
using System.Linq;
using Nml.Improve.Me.Dependencies;

namespace Nml.Improve.Me
{
	public class PdfApplicationDocumentGenerator : IApplicationDocumentGenerator
	{
		private readonly IDataContext DataContext;
		private IPathProvider _templatePathProvider;
		public IViewGenerator View_Generator;
		internal readonly IConfiguration _configuration;
		private readonly ILogger<PdfApplicationDocumentGenerator> _logger;
		private readonly IPdfGenerator _pdfGenerator;

		public PdfApplicationDocumentGenerator(
			IDataContext dataContext,
			IPathProvider templatePathProvider,
			IViewGenerator viewGenerator,
			IConfiguration configuration,
			IPdfGenerator pdfGenerator,
			ILogger<PdfApplicationDocumentGenerator> logger)
		{
			if (dataContext != null)
				throw new ArgumentNullException(nameof(dataContext));
			
			DataContext = dataContext;
			_templatePathProvider = templatePathProvider ?? throw new ArgumentNullException("templatePathProvider");
			View_Generator = viewGenerator;
			_configuration = configuration;
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_pdfGenerator = pdfGenerator;
		}
		private string GetUrl(string baseUri,string pathString)
		{
            string path= _templatePathProvider.Get(pathString);
			return baseUri + path;
        }
		private string ReturnView(Application application,string baseUri)
		{
			string url;
            ApplicationViewModel viewModel = new ApplicationViewModel()
			{
				ReferenceNumber=application.ReferenceNumber,
				State=application.State.ToDescription(),
				FullName= $"{application.Person.FirstName} {application.Person.Surname}",
				AppliedOn=application.Date,
				SupportEmail= _configuration.SupportEmail,
				Signature=_configuration.Signature
            };
			if (application.State== ApplicationState.Activated|| application.State == ApplicationState.InReview)
			{
				viewModel.LegalEntity = application.IsLegalEntity ? application.LegalEntity : null;
				viewModel.PortfolioFunds = application.Products.SelectMany(p => p.Funds);
				viewModel.PortfolioTotalAmount = application.Products.SelectMany(p => p.Funds)
														.Select(f => (f.Amount - f.Fees) * _configuration.TaxRate)
														.Sum();
            }
			switch(application.State)
			{
                case ApplicationState.Pending:
					url = GetUrl(baseUri, "PendingApplication");
					return View_Generator.GenerateFromPath(url, (PendingApplicationViewModel)viewModel);
				case ApplicationState.Activated:
                    url = GetUrl(baseUri, "ActivatedApplication");
                    return View_Generator.GenerateFromPath(url, (ActivatedApplicationViewModel)viewModel);
				case ApplicationState.InReview:
                    url = GetUrl(baseUri, "InReviewApplication");
                    InReviewApplicationViewModel inReviewApplicationViewModel=(InReviewApplicationViewModel)viewModel;
                    inReviewApplicationViewModel.InReviewMessage = "Your application has been placed in review" +
                                        application.CurrentReview.Reason switch
                                        {
                                            { } reason when reason.Contains("address") =>
                                                " pending outstanding address verification for FICA purposes.",
                                            { } reason when reason.Contains("bank") =>
                                                " pending outstanding bank account verification.",
                                            _ =>
                                                " because of suspicious account behaviour. Please contact support ASAP."
                                        };
                    inReviewApplicationViewModel.InReviewInformation = application.CurrentReview;
                    return View_Generator.GenerateFromPath(url,inReviewApplicationViewModel);
				default:
                    _logger.LogWarning($"The application is in state '{application.State}' and no valid document can be generated for it.");
                    return "";
            }
		}

		public byte[] Generate(Guid applicationId, string baseUri)
		{
			Application application = DataContext.Applications.Single(app => app.Id == applicationId);
            if (baseUri.EndsWith("/"))
                baseUri = baseUri.Substring(baseUri.Length - 1);

            if (application != null)
			{
				string view = ReturnView(application, baseUri);
				if (view=="")
				{
					return null;
				}
				else
				{
                    var pdfOptions = new PdfOptions
                    {
                        PageNumbers = PageNumbers.Numeric,
                        HeaderOptions = new HeaderOptions
                        {
                            HeaderRepeat = HeaderRepeat.FirstPageOnly,
                            HeaderHtml = PdfConstants.Header
                        }
                    };
                    var pdf = _pdfGenerator.GenerateFromHtml(view, pdfOptions);
                    return pdf.ToBytes();
                }
			}
			else
			{
				_logger.LogWarning($"No application found for id '{applicationId}'");
				return null;
			}
		}
	}
}
