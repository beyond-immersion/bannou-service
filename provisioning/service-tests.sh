docker exec -i provisioning-bannou-1 bash
response=$(curl --write-out '%{http_code}' --silent --output /dev/null 'http://127.0.0.1/testing/run/basic')
exit
echo "API response:" response
if [ response == 200 ]; then
exit
fi
exit 1
