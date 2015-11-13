using System.Web;
using System.Web.Mvc;

namespace AppSoftware.SignalRFileUploader.Controllers
{
    public class UploadController : Controller
    {
        [HttpGet]
        public ActionResult Upload()
        {
            return View();
        }

        [HttpGet]
        public ActionResult UploadFormIframe()
        {
            return View();
        }
    }
}
