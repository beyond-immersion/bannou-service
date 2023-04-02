docker exec -i provisioning_bannou_1 bash
response=$(curl --write-out '%{http_code}' --silent --output /dev/null '127.0.0.1:80/testing/run/basic')
exit
echo "API response:" response
if [ response == 200 ]; then
    exit
fi
exit 1
