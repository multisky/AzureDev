﻿namespace DxRemember
{
    using System;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
    using Microsoft.Bot.Builder.Dialogs;
    using Microsoft.Bot.Connector;
    using System.IO;
    using Microsoft.WindowsAzure.Storage.Shared.Protocol;
    using System.Collections.Generic;

    [Serializable]
    internal class BizCardAttachDialog : IDialog<object>
    {
        private readonly string initalMessage = "사진으로 찍은 명함 이미지를 첨부해 주세요.";
        public async Task StartAsync(IDialogContext context)
        {
            await context.PostAsync(initalMessage);

            context.Wait(this.MessageReceivedAsync);
        }

        public virtual async Task MessageReceivedAsync(IDialogContext context, IAwaitable<IMessageActivity> argument)
        {
            var message = await argument;

            if (message.Attachments != null && message.Attachments.Any())
            {
                var attachment = message.Attachments.First();
                using (HttpClient httpClient = new HttpClient())
                {
                    // Skype attachment URLs are secured by a JwtToken, so we need to pass the token from our bot.
                    if (message.ChannelId.Equals("skype", StringComparison.InvariantCultureIgnoreCase) && new Uri(attachment.ContentUrl).Host.EndsWith("skype.com"))
                    {
                        var token = await new MicrosoftAppCredentials().GetTokenAsync();
                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }

                    // 1. 업로드된 이미지의 Stream 가져오기
                    var responseMessage = await httpClient.GetAsync(attachment.ContentUrl);
                    //var contentLenghtBytes = responseMessage.Content.Headers.ContentLength;

                    Stream contentStream = await responseMessage.Content.ReadAsStreamAsync();

                    // 2. 이미지를 BLOB에 저장
                    // 랜덤 이름을 만들어서 Azure Storage에 저장하기
                    string fileName = attachment.Name;

                    Random random = new Random();
                    int randomNumber = random.Next(100000, 1000000);
                    fileName = string.Format("{0}-{1}", randomNumber.ToString(), fileName);

                    Utils.Upload2ASS up = new Utils.Upload2ASS();
                    Uri fileUri = up.UploadFilesToAzureStorage(fileName, contentStream);

                    await context.PostAsync("사진이 업로드 되었습니다. 이제 분석을 요청합니다. 잠시만 기다려 주십시오...");

                    // 3. Cognitive Service의 OCR API 호출하기
                    Utils.OcrHelper ocr = new Utils.OcrHelper();
                    string content = await ocr.Process(context, fileUri.ToString());

                    // 4. 정규표현식으로 필요한 정보 추출하기
                    Utils.RegUtils reg = new Utils.RegUtils();
                    List<string> msgs = reg.ExtractAndFormatData(content);

                    await context.PostAsync("추출된 고객의 정보입니다");

                    msgs[0] = "이름 : " + msgs[0];
                    msgs[1] = "전화번호 : " + msgs[1];
                    msgs[2] = "전자메일 : " + msgs[2];

                    foreach (string s in msgs)
                    {
                        await context.PostAsync(s);
                    }

                    await context.PostAsync("상기 정보는 Microsoft Flow를 통해서 메일로 전송되었습니다.");
                    context.Done(0);
                    //await context.PostAsync($"Attachment of {attachment.ContentType} type and size of {contentLenghtBytes} bytes received.");
                }
            }
            else
            {
                await context.PostAsync("사진을 등록하지 않아서 작업이 취소되었습니다.");
                context.Done(0);
            }

            context.Wait(this.MessageReceivedAsync);
        }


        
    }
}