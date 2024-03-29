﻿
using Surging.Cloud.Common;
using Surging.Cloud.CPlatform;
using Surging.Cloud.CPlatform.EventBus.Events;
using Surging.Cloud.CPlatform.EventBus.Implementation;
using Surging.Cloud.CPlatform.Exceptions;
using Surging.Cloud.CPlatform.Ioc;
using Surging.Cloud.CPlatform.Runtime.Session;
using Surging.Cloud.CPlatform.Transport.Implementation;
using Surging.Cloud.KestrelHttpServer;
using Surging.Cloud.KestrelHttpServer.Internal;
using Surging.Cloud.ProxyGenerator;
using Surging.IModuleServices.Common;
using Surging.IModuleServices.Common.Models;
using Surging.IModuleServices.User;
using Surging.Modules.Common.Repositories;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Org.BouncyCastle.Security;

namespace Surging.Modules.Common.Domain
{
    [ModuleName("User")]
    public class UserService : ProxyServiceBase, IUserService
    {
        #region Implementation of IUserService
        private readonly UserRepository _repository;
        private readonly ISurgingSession _surgingSession;
        public UserService(UserRepository repository)
        {
            this._repository = repository;
            _surgingSession = NullSurgingSession.Instance;
        }

        public async Task<IDictionary<string, object>> Check(long? userId, string serviceId)
        {
            throw new AuthException("测试异常",StatusCode.UnAuthorized);
        }

        public async Task<string> GetUserName(int id)
        {
           var text= await this.GetService<IManagerService>().SayHello("fanly");
            return await Task.FromResult<string>(text);
        }

        public Task<bool> Exists(int id)
        {
            var loginUserId = _surgingSession.UserId;
            return Task.FromResult(true);
        }

       public Task<UserModel> GetUserById(Guid id)
        {
            return Task.FromResult(new UserModel {

            });
        }

        public Task<int> GetUserId(string userName)
        {
            var xid = RpcContext.GetContext().GetAttachment("xid");
            return Task.FromResult(1);
        }

        public Task<DateTime> GetUserLastSignInTime(int id)
        {
            return Task.FromResult(new DateTime(DateTime.Now.Ticks));
        }

        public Task<bool> Get(List<UserModel> users)
        {
            return Task.FromResult(true);
        }

        public Task<UserModel> GetUser(UserModel user)
        {
            return Task.FromResult(new UserModel
            {
                Name = "fanly",
                Age = 18
            });
        }

        public Task<bool> Update(int id, UserModel model)
        {
            return Task.FromResult(true);
        }

        public Task<bool> GetDictionary()
        {
            return Task.FromResult<bool>(true);
        }

        public async Task Try()
        {
            Console.WriteLine("start");
            await Task.Delay(5000);
            Console.WriteLine("end");
        }

        public Task TryThrowException()
        {
            throw new Exception("用户Id非法！");
        }

        public async Task PublishThroughEventBusAsync(IntegrationEvent evt)
        {
            Publish(evt);
            await Task.CompletedTask;
        }

        public Task<IDictionary<string,object>> Authentication(AuthenticationRequestData requestData)
        {
            if (requestData.UserName == "admin" && requestData.Password == "admin")
            {
                IDictionary<string, object> payload = new Dictionary<string,object>();
                payload.Add(ClaimTypes.UserId, 1);
                payload.Add(ClaimTypes.UserName, "admin");
                payload.Add(ClaimTypes.OrgId, 2);
                payload.Add(ClaimTypes.DataPermissionOrgIds, new long[] { 2,4});

                return Task.FromResult(payload);
            }
            throw new AuthException("用户名账号不正确");
        }

        public Task<IdentityUser> Save(IdentityUser requestData)
        {
            return Task.FromResult(requestData);
        }

        public Task<ApiResult<UserModel>> GetApiResult()
        {
            return Task.FromResult(new ApiResult<UserModel>() { Value = new UserModel { Name = "fanly" }, StatusCode = 200 });
        }

        public async Task<bool> UploadFile(HttpFormCollection form)
        {
            var files = form.Files;
            foreach (var file in files)
            {
                using (var stream = new FileStream(Path.Combine(AppContext.BaseDirectory, file.FileName), FileMode.OpenOrCreate))
                {
                   await stream.WriteAsync(file.File, 0, (int)file.Length);
                }
            }
            return true;
        }

        public Task<string> GetUser(List<int> idList)
        {
            return Task.FromResult("type is List<int>");
        }

        public async Task<Dictionary<string, object>> GetAllThings()
        {
            return await Task.FromResult(new Dictionary<string, object> { { "aaa", 12 } });
        }

        public async Task<IActionResult> DownFile(string fileName,string contentType)
        {
            string uploadPath = Path.Combine(AppContext.BaseDirectory, fileName); 
            if (File.Exists(uploadPath))
            {
                using (var stream = new FileStream(uploadPath, FileMode.Open))
                {
                    var bytes = new Byte[stream.Length];
                    await stream.ReadAsync(bytes, 0, bytes.Length);
                    stream.Dispose();
                    return new FileContentResult(bytes, contentType, fileName);
                }
            }
            else
            {
                throw new FileNotFoundException(fileName);
            }
        }

        public async Task<Sex> SetSex(Sex sex)
        {
            return await Task.FromResult(sex);
        }

        public async Task<DeleteByIdOutput> Delete(DeleteByIdInput input)
        {
            return new DeleteByIdOutput  { Id = Guid.NewGuid().ToString(), Message = "删除成功" };
        }

        public async Task<string> Create(UserModel input)
        {
            return "创建用户成功";
        }
        #endregion Implementation of IUserService
    }
}