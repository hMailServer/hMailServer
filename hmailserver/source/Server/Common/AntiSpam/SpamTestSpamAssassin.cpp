/* Copyright (c) Martin/* Copyright (c) Martin Knafve / The hMailServer Community Developers (HCD) hMailServer.com */

#include <stdafx.h>
#include "SpamTestSpamAssassin.h"
#include "SpamTestData.h"
#include "SpamTestResult.h"
#include "AntiSpamConfiguration.h"
#include "SpamAssassin/SpamAssassinClient.h"

#include "../TCPIP/IOService.h"
#include "../TCPIP/TCPConnection.h"
#include "../TCPIP/SslContextInitializer.h"
#include "../TCPIP/DNSResolver.h"

#include "../BO/MessageData.h"
#include "../BO/Message.h"
#include "../Util/event.h"
#include "../Persistence/PersistentMessage.h"


#ifdef _DEBUG
#define DEBUG_NEW new(_NORMAL_BLOCK, __FILE__, __LINE__)
#define new DEBUG_NEW
#endif

namespace HM
{
   String 
   SpamTestSpamAssassin::GetName() const
   {
      return "SpamTestSpamAssassin";
   }

   bool 
   SpamTestSpamAssassin::GetIsEnabled()
   {
      AntiSpamConfiguration &config = Configuration::Instance()->GetAntiSpamConfiguration();
      if (config.GetSpamAssassinEnabled())
         return true;
      else
         return false;
   }

   std::set<std::shared_ptr<SpamTestResult> >
   SpamTestSpamAssassin::RunTest(std::shared_ptr<SpamTestData> pTestData)
   {
      std::set<std::shared_ptr<SpamTestResult> > setSpamTestResults;
      
      AntiSpamConfiguration& config = Configuration::Instance()->GetAntiSpamConfiguration();

      std::shared_ptr<Message> pMessage = pTestData->GetMessageData()->GetMessage();
      const String sFilename = PersistentMessage::GetFileName(pMessage);
      
      std::shared_ptr<IOService> pIOService = Application::Instance()->GetIOService();

      bool testCompleted;

      std::shared_ptr<Event> disconnectEvent = std::shared_ptr<Event>(new Event());
      std::shared_ptr<SpamAssassinClient> pSAClient = std::shared_ptr<SpamAssassinClient>(new SpamAssassinClient(sFilename, pIOService->GetIOService(), pIOService->GetClientContext(), disconnectEvent, testCompleted));
      
      String sHost = config.GetSpamAssassinHost();
      int iPort = config.GetSpamAssassinPort();

      
      DNSResolver resolver;

      std::vector<String> ip_addresses;
      resolver.GetIpAddresses(sHost, ip_addresses);

      String ip_address;
      if (ip_addresses.size())
      {
         ip_address = *(ip_addresses.begin());
      }
      else
      {
         String message = "The IP address for SpamAssassin could not be resolved. Aborting tests.";
         ErrorManager::Instance()->ReportError(ErrorManager::High, 5507, "SpamAssassinTestConnect::TestConnect", message);
         return setSpamTestResults;  
      }

      // Here we handle of the ownership to the TCPIP-connection layer.
      if (pSAClient->Connect(ip_address, iPort, IPAddress()))
      {
         // Make sure we keep no references to the TCP connection so that it
         // can be terminated whenever. We're longer own the connection.
         pSAClient.reset();

         disconnectEvent->Wait();
      }
      
      if (!testCompleted)
      {
         ErrorManager::Instance()->ReportError(ErrorManager::High, 5508, "SpamAssassinTestConnect::TestConnect", 
            "The SpamAssassin tests did not complete. Please confirm that the configuration (host name and port) is valid and that SpamAssassin is running.");

         return setSpamTestResults;  
      }

      // Check if the message is tagged as spam.
      std::shared_ptr<MessageData> pMessageData = pTestData->GetMessageData();
      pMessageData->RefreshFromMessage();

      bool bIsSpam = false;
      AnsiString sSpamStatus = pMessageData->GetFieldValue("X-Spam-Status");
      if (sSpamStatus.Mid(0, 3).ToUpper() == "YES")
         bIsSpam = true;

      if (bIsSpam)
      {
         int iScore = 0;
         if (config.GetSpamAssassinMergeScore())
            iScore = ParseSpamAssassinScore_(sSpamStatus);
         else
            iScore = config.GetSpamAssassinScore();

         String sMessage = "Tagged as Spam by SpamAssassin";
         std::shared_ptr<SpamTestResult> pResult = std::shared_ptr<SpamTestResult>(new SpamTestResult(GetName(), SpamTestResult::Fail, iScore, sMessage));
         setSpamTestResults.insert(pResult);   
      }
      
      return setSpamTestResults;
   }

   int 
   SpamTestSpamAssassin::ParseSpamAssassinScore_(const AnsiString &sHeader)
   {
      int iStartPos = sHeader.FindNoCase("score=");
      if (iStartPos < 0)
         return 0;

      iStartPos += 6;

      int iScoreEnd = sHeader.Find(".", iStartPos);
      if (iScoreEnd < 0)
         return 0;

      int iScoreLength = iScoreEnd - iStartPos;

      if (iScoreLength <= 0)
         return 0;

      AnsiString sScore = sHeader.Mid(iStartPos, iScoreLength);

      int iRetVal = atoi(sScore);
      return iRetVal;

   }




}